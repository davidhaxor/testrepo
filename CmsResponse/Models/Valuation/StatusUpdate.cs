using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Xml;
using System.Text.RegularExpressions;
using GenProcs.MyDbl;
using GenProcs.Utils;
using log4net;
using ResNet.CMS.Orders;

namespace CmsResponse.Models.Valuation
{
    public class StatusUpdate
    {
        public string responseXml { get; set; }

        private static readonly log4net.ILog log = LogManager.GetLogger( typeof( StatusUpdate ) );
        private StatusUpdateRq statusMsg { get; set; }
        private StandardResponse response { get; set; }
        private string errMsg;
        private List<String> listFileMsgs;
        private string valuationOrderId;
        private int property_id;
        private int task_id;
        private int res_id;
        private int filesDownloaded;
        private Config config;
        private const string downloadFailed = "NOT downloaded";
        private bool reportDownloaded;
        private Dictionary<string, string> dictFileExts;
        private string UCDPRReadyFileName;

        private class Config
        {
            public string saveBpoPdfDir { get; set; }
            public string saveAprPdfDir { get; set; }
            public string saveFileTypes { get; set; }
            public string tempFileDir { get; set; }
            public int timeOut { get; set; }
            public string accept { get; set; }
            public string contentType { get; set; }
            public string mediaType { get; set; }
            public int maxFS { get; set; }
            public string fileExts { get; set; }
        }

        #region SqlStmts

        private const string selVendorTrans = @"select cast(concat(
  'bpo_',val.bpo_request_id
,'\t', val.status
,'\t', v.their_user_id
,'\t', v.their_password
,'\t', val.property_id
,'\t', val.task_id
,'\t', val.res_id
  ) as char)  x
from resnet_if.bpo_credentials v
left join resnet_if.bpo_requests val on val.vendor=v.vendor
where v.vendor='CMS'
  and val.status='open'
  and val.vendor_order_no='{0}'
  and val.loan_no='{1}'";

        private const string selTaskType = @"select task_type
from resnet.property_tasks
where task_id = {0}";

        private const string selReasonForReturn = @"select reason_for_return
from {0}
where {1} = {2}";

        private const string setBpoPrice = @"update {0} 
set asis_value = {1}, repaired_value = {2}
where {3} = {4}";

        private const string updateReviseDate = @"update resnet.property_details 
set {0} = now()
where property_id = {1}";

        private const string updOrderRequest =
"update resnet_if.bpo_requests" + 
" set sent=now(), status='{0}', file=ifnull({1},file)," +
" log=trim('\n' from trim(concat( now(), '{2}', '\n\n', ifnull(trim('\n' from trim(log)),''))))" +
" where bpo_request_id={3} and vendor='CMS'";

        private const string updTaskSql = 
"update resnet.property_tasks set status='R', submit_dt=ifnull(submit_dt,now()), vendor='CMS', respon='M' " +
"where task_id={0} and property_id={1} and status='A' and respon='V'";

        private const string addPropUploadSql =
"insert into resnet.property_uploads ( file_no, task_id, property_id, res_id, file_name, uploadfile_uuid, document_type )" +
" values (5,{0},{1},{2},'{3}','{4}','{5}')" + // 0=tid, 1=pid, 2=rid, 3=fileName, 4=guid, 5=docType
" on duplicate key update file_name=values(file_name) ";

        #endregion

        public StatusUpdate()
        {
        }

        public void SetConfig( Dictionary<string, string> dict )
        {
            config = new Config()
            {
                saveBpoPdfDir = dict.GetValueDef( "valOrdSaveBpoPdfDir" ),
                saveAprPdfDir = dict.GetValueDef( "valOrdSaveAprPdfDir" ),
                saveFileTypes = "," + dict.GetValueDef( "valOrdSaveFileTypes" ).ToLower() + ",",
                tempFileDir   = dict.GetValueDef( "valOrdTempFileDir" ),
                timeOut       = dict.GetValueDef( "valOrdRequestTimeoutMs" ).ToStringDef( "" ).ToIntDef( 30000 ),
                accept        = dict.GetValueDef( "valOrdRequestAccept" ),
                contentType   = dict.GetValueDef( "valOrdRequestContentType" ),
                mediaType     = dict.GetValueDef( "valOrdRequestMediaType" ),
                maxFS         = dict.GetValueDef( "valOrdMaxFileSize" ).ToStringDef( "" ).ToIntDef( 0 ),
                fileExts      = dict.GetValueDef( "valOrdSaveFileExts" ).ToStringDef( "" )
            };
        }

        public bool Execute( string xmlText )
        {
            errMsg = "";

            response = new StandardResponse();
            response.ResponseHdr = new ResponseHdrType()
            {
                Code = ResponseHdrStatusType.ApplicationError,
                Message = errMsg
            };

            try
            {
                statusMsg = xmlText.Deserialize<StatusUpdateRq>();
                ProcessStatusMsg();
            }
            catch ( Exception e )
            {
                False( "Error in document uploaded: " + e.Message, e.ToString() );
            }

            response.ResponseHdr.Message = errMsg;
            response.ResponseHdr.Code = errMsg.IsStrEmpty()
                ? ResponseHdrStatusType.OK
                : ResponseHdrStatusType.ApplicationError;

            responseXml = response.SerializeObject<StandardResponse>();

            return false;
        }

        private bool False( string msg, string logMsg = null )
        {
            StackFrame frame = new StackFrame( 1 );
            var method = frame.GetMethod();
            var type = method.DeclaringType;
            var name = method.Name;

            log.ErrorFormat( name + ": " + logMsg == null ? msg : logMsg  );
            errMsg = msg;
            return false;
        }

        private bool ProcessStatusMsg()
        {
            if ( statusMsg == null || statusMsg.Header == null || statusMsg.StatusUpdate == null )
            {
                return False( "No valid StatusUpdateRq found." );
            }
            else if ( ! GetValuationRequest() ) return false;
            else if ( IsValuationDone() )
            {
                ValuationDone();
            }
            else
            {
                ValuationUpdate( false );
            }

            return true;
        }

        private bool IsValuationDone()
        {
            if ( statusMsg == null || statusMsg.StatusUpdate == null || statusMsg.StatusUpdate.StatusId == null ) return false;

            return "LA,LC,LD".InList( statusMsg.StatusUpdate.StatusId );
        }

        private bool GetValuationRequest()
        {
            var hdr = statusMsg.Header;
            var ic = StringComparison.InvariantCultureIgnoreCase;
            string vendorOrderNo = statusMsg.StatusUpdate.DocId;

            if ( hdr.AppInstanceLogin.IsStrEmpty() || hdr.AppInstancePwd.IsStrEmpty() ) return false;

            string sql = selVendorTrans + " union " + selVendorTrans.Replace( "bpo", "apr" );
            string info = Db.Instance.SqlFunc( sql.FormatWith(
                vendorOrderNo,
                statusMsg.StatusUpdate.LoanNumber
                ) );

            string[] arr = info.Split( new char[] { '\t' } ); //0=type_id, 1=status, 2=userId, 3=pwd, 4=pid, 5=tid

            if ( arr.Length != 7 || arr[ 0 ].IsEmpty() ) return False( "No Transaction found" );

            if ( ! arr[ 2 ].Equals( hdr.AppInstanceLogin, ic ) ||
                 ! arr[ 3 ].Equals( hdr.AppInstancePwd, ic ) ) return False( "Credentials invalid" );

            if ( ! arr[ 1 ].Equals( "open", ic ) ) False( "Transaciton is : " + arr[ 1 ] );

            valuationOrderId = arr[ 0 ];
            property_id      = arr[ 4 ].ToIntDef( 0 );
            task_id          = arr[ 5 ].ToIntDef( 0 );
            res_id           = arr[ 6 ].ToIntDef( 0 );
            return true;
        }

        private bool ValuationDone()
        {
            var upd = statusMsg.StatusUpdate;

            listFileMsgs = new List<String>();
            reportDownloaded = false;
            filesDownloaded = 0;

            if ( upd.ReportUrl.HasValue() ) reportDownloaded = DownloadFileType( upd.ReportUrl, "Report" );
            if ( upd.UCDPReadyFileURL.HasValue() ) DownloadFileType( upd.UCDPReadyFileURL, "UCDPReadyFile" );

            if ( upd.AssociatedFiles != null )
                foreach ( var item in upd.AssociatedFiles )
                {
                    if ( upd.ReportUrl.HasValue() ) DownloadFileType( item.FileUrl, item.FileType );
                }

            if ( filesDownloaded == 0 )
            {
                return False( "No File Elements found in XML to download (No Report, UCDPReadyFile or AssociatedFiles)" );
            }

            string msgs = " Downloads: " + String.Join(", ",listFileMsgs.ToArray());

            bool lbDone = msgs.IndexOf( downloadFailed ) == -1;

            if ( ! ValuationUpdate( lbDone, msgs ) ) return false;

            return true;
        }

        private bool DownloadFileType( string fileUrl, string fileType )
        {
            if ( ! config.saveFileTypes.Contains( "," + fileType.ToLower() + "," ) )
            {
                return false;
            }
            try
            {
                if ( dictFileExts == null )
                    dictFileExts = new Dictionary<string, string>( StringComparer.InvariantCultureIgnoreCase );

                foreach ( var item in config.fileExts.Split( new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries ) )
                {
                    string[] kv = item.Split( new char[] { '=' }, 2, StringSplitOptions.RemoveEmptyEntries );
                    if ( kv.Length == 2 && !dictFileExts.ContainsKey( kv[ 0 ].Trim() ) )
                        dictFileExts[ kv[ 0 ].Trim() ] = kv[ 1 ].TrimStart( new char[] { '.', ' ' } );
                }

                string fileExt = dictFileExts.ContainsKey( fileType ) ? dictFileExts[ fileType ] : "";
                
                if ( fileExt.IsEmpty() )
                    switch ( fileType )
                    {
                        case "Report"                       : fileExt = "pdf"; break;
                        case "UCDPReadyFile"                : fileExt = "UCDPRReady.xml"; break;
                        case "Review Scope"                 : fileExt = "ReviewScope.pdf"; break;
                        case "Valuation Independence Letter": fileExt = "ValIndLetter.pdf"; break;
                        case "Customer Invoice (System)"    : fileExt = "Invoice.pdf"; break;
                        case "Appraisal Invoice"            : fileExt = "AprInvoice.pdf"; break;
                        case "Other Documents"              : fileExt = "Other.pdf"; break;
                        case "Review"                       : fileExt = "Review.pdf"; break;
                        default:
                            fileExt = "pdf";
                            break;
                    }

                string saveAs = "{0}_{1}.{2}".FormatWith( property_id, task_id, fileExt );

                string fileName = valuationOrderId.StartsWith( "apr" )
                    ? Path.Combine( config.saveAprPdfDir, saveAs )
                    : Path.Combine( config.saveBpoPdfDir, saveAs );

                if ( GetFile(fileUrl, fileName, out errMsg ))
                {
                    if (fileType == "UCDPReadyFile")
                    {
                        UCDPRReadyFileName = fileName;
                    }

                    listFileMsgs.Add( "{0} {1}".FormatWith( fileType, saveAs ) );
                    filesDownloaded++;
                    return true;
                }
                else 
                    listFileMsgs.Add( "{0} {1} {2}".FormatWith( fileType, downloadFailed, errMsg ) );
            }
            catch ( Exception e )
            {
                listFileMsgs.Add( "{0} {1}. error: {2}".FormatWith( fileType, downloadFailed, e.Message ) );
            }
            return false;
        }

        public bool GetFile( string url, string saveToFilename, out string errMsg )
        {
            errMsg = "";
            string err = null;

// testing url = "https://www.aiready.com/envtool/scripts_low/retrieve.asp?file=C99A66AADDE8%2DRERKAUS%2EPDF";

            Func<HttpWebRequest> callBack = ( () =>
            {
                var request = ( HttpWebRequest ) WebRequest.Create( url );

                request.Method            = "GET";
                request.AllowAutoRedirect = false;
                request.Timeout           = config.timeOut;
                request.Accept            = config.accept;
                request.ContentType       = config.contentType;
                request.MediaType         = config.mediaType;

                return request;
            } );

            if ( wp.SaveToFileNameFromUrl( saveToFilename, out err, callBack, maxFileSize: config.maxFS ) )
            {
                return File.Exists( saveToFilename );
            }
            else
            {
                errMsg = "SaveToFileNameFromUrl download failed2: {0} file: {1}".FormatWith( err, Path.GetFileName( saveToFilename ) );
            }
            return false;
        }

        private bool ValuationUpdate( bool orderDone, string msgs = "" )
        {
            try
            {
                var upd = statusMsg.StatusUpdate;
                int valId = valuationOrderId.RightOf( "_" ).ToIntDef( 0 );
                string pid_task = "'{0}_{1}.pdf'".FormatWith( property_id, task_id );
                string msg = " {0}: <DocId>{1}</DocId> <LoanNumber>{2}</LoanNumber><StatusId>{3}</StatusId><Description>{4}</Description>{5}"
                            .FormatWith(
                                ( orderDone ? "StatusDone" : "StatusUpdate" ),
                                upd.DocId,
                                upd.LoanNumber,
                                upd.StatusId,
                                upd.Description,
                                msgs
                            );

                string updOrderSql = updOrderRequest;

                if ( valuationOrderId.StartsWith( "apr" ) ) updOrderSql = updOrderSql.ReplaceStr( "bpo_", "apr_" );

                Db.Instance.SqlExec( updOrderSql.FormatWith(
                      ( orderDone ? "done" : "open" )  //status
                    , ( orderDone ? pid_task : "null" ) //file
                    , DbUtils.Escape( msg )
                    , valId //order_id
                    ) );

                if ( orderDone )
                {
                    Db.Instance.SqlExec( updTaskSql.FormatWith(
                        task_id,
                        property_id                                                    
                        ));

                    if ( reportDownloaded )
                    {
                        string docType = valuationOrderId.StartsWith( "apr" ) ? "Appraisal" : "BPO";
                        Db.Instance.SqlExec( addPropUploadSql.FormatWith(
                            task_id,        //0=tid, 1=pid, 2=rid, 3=fileName, 4=guid, 5=docType
                            property_id,
                            res_id,
                            DbUtils.Escape( pid_task.Replace( "'","" ) ),
                            DbUtils.Escape( Guid.NewGuid().ToString() ),
                            DbUtils.Escape( docType )
                            ) );
                    }
                }

                string taskType = Db.Instance.SqlFunc(selTaskType.FormatWith(
                    task_id
                    ));

                try
                {
                    if (!string.IsNullOrWhiteSpace(UCDPRReadyFileName) && orderDone)
                    {

                        string UCDPxml = File.ReadAllText(UCDPRReadyFileName);
                        XmlDocument xmlDoc = new XmlDocument();
                        xmlDoc.LoadXml(UCDPxml);

                        if (taskType == "BPO2")
                        {
                            string AsisValue = xmlDoc.DocumentElement.SelectSingleNode("//NORMALMKTTIME/ASIS/AMT").InnerText;
                            string RepairedValue = xmlDoc.DocumentElement.SelectSingleNode("//REPAIRED/AMT").InnerText;
                            AsisValue = string.IsNullOrWhiteSpace(AsisValue) ? "0" : decimal.Parse(Regex.Replace(AsisValue, @"[^\d.]", "")).ToString("#.##");
                            RepairedValue = string.IsNullOrWhiteSpace(RepairedValue) ? "0" : decimal.Parse(Regex.Replace(RepairedValue, @"[^\d.]", "")).ToString("#.##");

                            Db.Instance.SqlExec(setBpoPrice.FormatWith(
                                "resnet_if.bpo_requests"
                                , AsisValue
                                , RepairedValue
                                , "bpo_request_id"
                                , valId
                                ));
                        }

                    }

                    //Set revised date based on ReasonForReturn being entered.
                    if (orderDone)
                    {
                        if (taskType == "BPO2")
                        {
                            string reasonForReturn = Db.Instance.SqlFunc(selReasonForReturn.FormatWith(
                                "resnet_if.bpo_requests"
                                , "bpo_request_id"
                                , valId
                                ));

                            if (!string.IsNullOrWhiteSpace(reasonForReturn))
                            {
                                Db.Instance.SqlExec(updateReviseDate.FormatWith(
                                    "bpo2_revise_dt"
                                    , property_id
                                    ));
                            }
                        }

                        if (taskType == "APR" || taskType == "APRU")
                        {
                            string reasonForReturn = Db.Instance.SqlFunc(selReasonForReturn.FormatWith(
                                "resnet_if.apr_requests"
                                , "apr_request_id"
                                , valId
                                ));

                            if (!string.IsNullOrWhiteSpace(reasonForReturn))
                            {
                                if (taskType == "APR")
                                {
                                    Db.Instance.SqlExec(updateReviseDate.FormatWith(
                                        "apr_revise_dt"
                                        , property_id
                                        ));
                                }
                                if (taskType == "APRU")
                                {
                                    Db.Instance.SqlExec(updateReviseDate.FormatWith(
                                        "apru_revise_dt"
                                        , property_id
                                        ));
                                }
                            }
                        }
                    }
                }
                catch(Exception e)
                {
                    return False("Error" + e.Message, e.ToString());
                }

                return true;
            }
            catch ( Exception e )
            {
                return False( "Error" + e.Message, e.ToString() );
            }
        }

    }
}