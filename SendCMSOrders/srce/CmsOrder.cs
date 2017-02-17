using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Xml.Linq;
using Devart.Data.MySql;
using GenProcs.Utils;
using log4net;

namespace ResNet.CMS.Orders
{
    public class CmsOrder
    {
        private static readonly log4net.ILog log = LogManager.GetLogger( typeof( CmsOrder ) );
        private string responseFile;
        private int valId;
        private RequestHdrType envOrderHeader;
        private CreateOrderRq orderXml;
        private CreateOrderRs orderResponse;
        private UpdateOrderStatusRq updOrderXml;
        private AddOrderCommentRQ updOrderCommentXml;

        #region internal Classes

        public class Config
        {
            public int maxFilesize { get; set; }
            public string tempFileDir { get; set; }
            public bool saveTempFiles { get; set; }
            public string url { get; set; }
            public string fileUrl { get; set; }
            public int requestTimeoutMs { get; set; }
            public string requestAccept { get; set; }
            public string requestContentType { get; set; }
            public string requestMediaType { get; set; }
            public string savePdfDir { get; set; }
            public string saveEnvDir { get; set; }
            public string userId { get; set; }
            public string passwd { get; set; }
            public string bpoPicDir { get; set; }
            public int customerPk { get; set; }
            public int groupId { get; set; }
            public int procPrsnPk { get; set; }
            public bool IsTest { get; set; }
        }


        #endregion

        public enum ValType { bpo, apr }
        public ValType valuation { get; set; }
        public Config config { get; set; }
        public string envFileName { get; set; }
        public string sendParentDocId { get; set; }
        public bool sendEnvFile { get; set; }
        public bool envFileWasSent { get; private set; }
        public string errorMsg { get; set; }
        public List<String> resultsLog { get; set; }
        public string vendorOrderNo { get; set; }
        public string envDocId { get; set; }

        private MySqlDataReader dataset;
        private Dictionary<string, int> flds;
        private List<string> nof = new List<string>();
        private string envFP;
        private const string respOrdFileName = "CMSValOrd_{0}_{1}_response.xml";
        private const string respEnvFileName = "CMSEnvUpl_{0}_{1}_response.xml";
        private const string ts = "yyyyMMdd_HHmmss";
        private bool IsOrderAnUpdate;

        public CmsOrder()
        {
            resultsLog = new List<string>();
        }

        public CmsOrder( Dictionary<string, string> dictConfig )
        {
            SetConfig( dictConfig );
        }

        public bool Execute( int orderId, MySqlDataReader dr, bool isApr )
        {
            dataset = dr;
            errorMsg = "";
            resultsLog = new List<string>();

            valuation =  isApr ? CmsOrder.ValType.apr : CmsOrder.ValType.bpo;

            // 1. dbInfo to class/XML

            orderXml = null;
            updOrderXml = null;
            updOrderCommentXml = null;

            OrderData2Xml( orderId );

            if ( sendParentDocId.HasValue() )
            {
                orderXml.Order.ParentDocID = sendParentDocId;
            }

            if ( errorMsg.HasValue() ) return false;

            // 2. send request

            if ( IsOrderAnUpdate )
            {
                if ( ! SendOrderUpdate( orderId ) ) return false;

                updOrderCommentXml.Header.MessageId = Guid.NewGuid().ToString();
                updOrderCommentXml.Header.CorrelationId = Guid.NewGuid().ToString();
                updOrderCommentXml.Header.SequenceKey = Guid.NewGuid().ToString();

                if ( ! SendOrderComment( orderId ) ) return false;
            }
            else
            {
                if ( ! SendOrderRequest( orderId ) ) return false;
            }

            return errorMsg.IsStrEmpty();
        }

        public bool SendEnvAsOrder( int orderId, MySqlDataReader dr, string filePath )
        {

            dataset = dr;
            errorMsg = "";
            resultsLog = new List<string>();

            // 1. dbInfo to class/XML

            orderXml = null;
            OrderData2Xml( orderId );

            orderXml.Order.ServiceId = ServiceOption.Item102;

            // 2. sendEnvFile 

            if ( ! SendOrderRequest( orderId, "eo" ) ) return false;

            envDocId = orderResponse.Tracking.DocId;  //env order is the Parent Doc Id in CMS for all subsequent orders

            envOrderHeader = orderXml.Header;
            envOrderHeader.MessageId = Guid.NewGuid().ToString();

            envFileWasSent = UploadEnvFile( orderId, filePath );

            return errorMsg.IsStrEmpty();
        }

        private bool SendOrderRequest( int orderId, string fileType = "" )
        {
            orderResponse = PostOrderRequest( orderId, fileType );

            if ( errorMsg.HasValue() )
            {
                return false;
            }
            else if ( orderResponse == null || orderResponse.Header == null )
            {
                return False( "No Response from CMS: " + orderId.ToString() );
            }
            else if ( orderResponse.Header.Code != ResponseHdrStatusType.OK )
            {
                return False( orderResponse.Header.Message + " " + orderId.ToString() );
            }
            return true;
        }

        private bool SendOrderUpdate(int orderId, string fileType = "")
        {
            var responseOrderStatus = PostOrderUpdate(orderId, fileType);

            if ( errorMsg.HasValue() )
            {
                return false;
            }
            else if ( responseOrderStatus == null || responseOrderStatus.ResponseHdr == null )
            {
                return False( "No Response from CMS: " + orderId.ToString() );
            }
            else if ( responseOrderStatus.ResponseHdr.Code != ResponseHdrStatusType.OK )
            {
                return False( responseOrderStatus.ResponseHdr.Message + " " + orderId.ToString() );
            }
            return true;
        }

        private bool SendOrderComment(int orderId, string fileType = "")
        {
            var responseOrderComment = PostOrderComment(orderId, fileType);

            if ( errorMsg.HasValue() )
            {
                return false;
            }
            else if ( responseOrderComment == null || responseOrderComment.Header == null )
            {
                return False( "No Response from CMS: " + orderId.ToString() );
            }
            else if ( responseOrderComment.Header.Code != ResponseHdrStatusType.OK )
            {
                return False( responseOrderComment.Header.Message + " " + orderId.ToString() );
            }
            return true;
        }

        public static string CheckConfig( Dictionary<string, string> dictConfig )
        {
            var errs = new StringBuilder();

            string tempFileDir2 = dictConfig.GetValueDef( "cmsTempFileDir" );

            if ( ! Directory.Exists( tempFileDir2 ) ) errs.Append( ", Invalid cmsTempFileDir: " + tempFileDir2 );

            if ( errs.Length > 0 )
            {
                errs.Insert( 0, "Required CMS Val Order Config Values missing: " + errs.ToString().Substring( 1 ) );
            }
            return errs.ToString();
        }

        public void SetConfig( Dictionary<string, string> dictConfig )
        {
            config = new Config()
            {
                maxFilesize        = dictConfig.GetValueDef( "cmsMaxFilesize" ).ToIntDef( 100000 ),
                tempFileDir        = dictConfig.GetValueDef( "cmsTempFileDir" ),
                savePdfDir         = dictConfig.GetValueDef( "cmsSavePdfDir" ),
                saveTempFiles      = dictConfig.GetValueDef( "cmsSaveTempFiles" ).IsTrue(),
                url                = dictConfig.GetValueDef( "cmsUrl" ),
                fileUrl            = dictConfig.GetValueDef( "cmsFileUrl" ), 
                userId             = dictConfig.GetValueDef( "cmsUserId" ),
                passwd             = dictConfig.GetValueDef( "cmsPasswd" ),
                requestTimeoutMs   = dictConfig.GetValueDef( "cmsRequestTimeoutMs" ).ToIntDef( 120000 ),
                requestAccept      = dictConfig.GetValueDef( "cmsRequestAccept" ),
                requestContentType = dictConfig.GetValueDef( "cmsRequestContentType" ),
                requestMediaType   = dictConfig.GetValueDef( "cmsRequestMediaType" ),
                saveEnvDir         = dictConfig.GetValueDef( "envSaveEnvDir" ),
                customerPk         = dictConfig.GetValueDef( "cmsCustomerPK" ).ToIntDef(36901),
                groupId            = dictConfig.GetValueDef( "cmsGroupId" ).ToIntDef( 37118 ),
                procPrsnPk         = dictConfig.GetValueDef( "cmsProcPrsnPk" ).ToIntDef( 45759 ),
                IsTest             = dictConfig.GetValueDef( "IsTest" ).IsTrue(),
            };
        }

        private bool False( string msg )
        {
            var frame = new StackFrame( 1 );
            var method = frame.GetMethod();
            var type = method.DeclaringType;
            var name = method.Name;
            errorMsg = name + ": " + msg;
            return false;
        }

        private void OrderData2Xml( int orderId )
        {

            flds = new Dictionary<string, int>( 200, StringComparer.InvariantCultureIgnoreCase );
            for ( int i = 0; i < dataset.FieldCount; i++ )
            {
                var name = dataset.GetName( i );
                if ( flds.ContainsKey( name ) )
                    nof.Add( "dupSqlCol: " + name );
                else
                    flds.Add( name, i );
            }

            IsOrderAnUpdate = dr_GetString( "RequestStatus", "" ).ToLower() == "dispute";

            var o = new CreateOrderRqType();
            var h = new RequestHdrType();

            orderXml = new CreateOrderRq();
            orderXml.Order = o;
            orderXml.Header = h;

            h.IsTest           = config.IsTest;
            h.SourceApp        = dr_GetString( "SourceApp" );
            h.TargetApp        = dr_GetString( "TargetApp" );
            h.AppInstance      = dr_GetString( "AppInstance" );
            h.AppInstanceLogin = config.userId;
            h.AppInstancePwd   = config.passwd;
            h.Timestamp        = DateTime.Now;
            h.MessageId        = dr_GetString( "MessageId" ); // Guid.NewGuid().ToString();
            h.CorrelationId    = dr_GetString( "CorrelationId" ); // Guid.NewGuid().ToString();
            h.SequenceKey      = dr_GetString( "SequenceKey" ); // Guid.NewGuid().ToString();
            h.SequenceNumber   = dr_GetString( "SequenceNumber" ).ToIntDef( 1 ); // 1;

            o.GroupId             = config.groupId;
            o.GroupIdSpecified    = true;
            o.ProcPrsnPk          = config.procPrsnPk;
            o.ProcPrsnPkSpecified = true;

            o.ProcComments = dr_GetString( "ProcComments" ); 
            o.OrderInfo    = new OrderInfoType()
            {
                LoanNumber             = dr_GetString( "LoanNumber"             ), 
                Reference1             = dr_GetString( "Reference1"             ), 
                Reference2             = dr_GetString( "Reference2"             ),  
                IsRushOrder            = dr_GetString( "IsRushOrder"            ).IsTrue(),
                IsFHA                  = dr_GetString( "IsFHA"                  ).IsTrue(),
                SubLoanType            = dr_GetString( "SubLoanType"            ).StringToEnum<SubLoanTypeOption>( SubLoanTypeOption.Item203 ),
                SubLoanTypeSpecified   = dr_GetString( "SubLoanTypeSpecified"   ).IsTrue(),
                LoanClassificationCode = dr_GetString( "LoanClassificationCode" ),
                OrderAttributeCode     = dr_GetString( "OrderAttributeCode"     ).ToStringDef("").Split(new char[]{ ';' }, StringSplitOptions.RemoveEmptyEntries),
                CostCenterCode         = dr_GetString( "CostCenterCode"         ),
                IsHighValue            = dr_GetString( "IsHighValue"            ).IsTrue(),
                GovernmentCaseNumber   = dr_GetString( "GovernmentCaseNumber"   ),
                DueToCustomer          = dr_GetString( "DueToCustomer"          ).ToDateDef( DateTime.Now.AddDays( 5 )),
                DueToCustomerSpecified = dr_GetString( "DueToCustomerSpecified" ).IsTrue(),
                DueFromVendor          = dr_GetString( "DueFromVendor"          ).ToDateDef( DateTime.Now.AddDays( 5 ) ),
                DueFromVendorSpecified = dr_GetString( "DueFromVendorSpecified" ).IsTrue(),
                ClosingDt              = dr_GetString( "ClosingDt"              ).ToDateDef( DateTime.Now.AddDays( 5 ) ),
                ClosingDtSpecified     = dr_GetString( "ClosingDtSpecified"     ).IsTrue(),
                CustomerPk             = config.customerPk,
                CustomerPkSpecified    = true,
                LoanOfficer            = dr_GetString( "LoanOfficer"            ).ToIntDef( 0 ),
                LoanOfficerSpecified   = dr_GetString( "LoanOfficerSpecified"   ).IsTrue(),
                Channel                = dr_GetString( "Channel"                ).StringToEnum< ChannelOption>( ChannelOption.Item1),
                ApplicantFirstName     = dr_GetString( "ApplicantFirstName"     ),
                ApplicantMiddleInitial = dr_GetString( "ApplicantMiddleInitial" ),
                ApplicantLastName      = dr_GetString( "ApplicantLastName"      ),
                ApplicantCompanyName   = dr_GetString( "ApplicantCompanyName"   ),
            };

            string amgr = dr_GetString( "AMFirstName" ).IsStrEmpty() ? "" : ", {0} {1}, {2} {3}".FormatWith(
                                dr_GetString( "AMFirstName" ),
                                dr_GetString( "AMLastName" ),
                                dr_GetString( "AMPhone" ),
                                dr_GetString( "AMEmail" )
                            );
            o.PropertyAddress = new AddressInfoType()
            {
                RequireNormalization      = dr_GetString( "RequireNormalization" ).IsTrue(),   
                UnstructuredStreetAddress = dr_GetString( "UnstructuredStreetAddress" ), 
                StreetNo                  = dr_GetString( "StreetNo","" ),
                Prefix                    = dr_GetString( "Prefix", "" ),
                Street                    = dr_GetString( "Street", "" ),
                Suffix                    = dr_GetString( "Suffix", "" ),
                UnitNo                    = dr_GetString( "UnitNo", "" ),
                City                      = dr_GetString( "City", "" ),
                County                    = dr_GetString( "County", "" ),                     
                State                     = dr_GetString( "State" ).StringToEnum<State>( State.CA ),
                Zip                       = dr_GetString( "Zip", "" ),
                AccessInstruction         = "{0}{1}".FormatWith( dr_GetString( "AccessInstruction", "" ).Left( 2850 ), amgr )
            };
            o.PropertyTypeId                 = dr_GetString( "PropertyTypeId"        );
            o.LoanPurposeId                  = dr_GetString( "LoanPurposeId"         ).StringToEnum< LoanPurposeOption>( LoanPurposeOption.Item);
            o.HpmlOrder                      = dr_GetString( "HpmlOrder "            ).IsTrue();
            o.HpmlOrderSpecified             = dr_GetString( "HpmlOrderSpecified"    ).IsTrue();                
            o.OccupancyTypeId                = dr_GetString( "OccupancyTypeId"       ).StringToEnum<OccupancyTypeOption>( OccupancyTypeOption.VAC);
            o.OwnerEstimate                  = dr_GetString( "OwnerEstimate"         ).ToDecimalDef( 0 );
            o.OwnerEstimateSpecified         = dr_GetString( "OwnerEstimateSpecified").IsTrue();                
            o.SalesPrice                     = dr_GetString( "SalesPrice"            ).ToDecimalDef( 0 );
            o.SalesPriceSpecified            = dr_GetString( "SalesPriceSpecified"   ).IsTrue();                
            o.OriginalPurchasePrice          = dr_GetString( "OriginalPurchasePrice" ).ToDecimalDef( 0 );
            o.OriginalPurchasePriceSpecified = dr_GetString( "OriginalPurchasePriceSpecified" ).IsTrue();                
            o.LoanAmount                     = dr_GetString( "LoanAmount"              ).ToDecimalDef( 0 );
            o.LoanAmountSpecified            = dr_GetString( "LoanAmountSpecified"     ).IsTrue();                
            o.OutstandingLien                = dr_GetString( "OutstandingLien   "      ).ToDecimalDef( 0 );
            o.OutstandingLienSpecified       = dr_GetString( "OutstandingLienSpecified").IsTrue();                
            o.CustomerPrice                  = dr_GetString( "CustomerPrice"           ).ToDecimalDef( 0 );
            o.CustomerPriceSpecified         = dr_GetString( "CustomerPriceSpecified"  ).IsTrue();                
            o.ProviderFee                    = dr_GetString( "ProviderFee"             ).ToDecimalDef( 0 );
            o.ProviderFeeSpecified           = dr_GetString( "ProviderFeeSpecified"    ).IsTrue();                
            o.ServiceId                      = dr_GetString( "ServiceId"               ).StringToEnum<ServiceOption>( ServiceOption.Item1);
            o.ServiceProviderID              = null;//dr_GetString( "ServiceProviderID"       );

            o.BorrowerContact = new ContactType() {

                Name = dr_GetString( "Contact_Name" ),
                ContactTypeId  = dr_GetString( "Contact_ContactTypeId" ).StringToEnum<ContactTypeIdOption>( ContactTypeIdOption.E ),
                StreetAddress  = dr_GetString( "Contact_StreetAddress" ),             
                City           = dr_GetString( "Contact_City" ),             
                State          = dr_GetString( "Contact_State" ).StringToEnum<State>( State.CA ),
                StateSpecified = dr_GetString( "Contact_StateSpecified" ).IsTrue(),               
                Zip            = dr_GetString( "Contact_Zip" ),
                County         = dr_GetString( "Contact_County" ),
                Phone          = dr_GetString( "Contact_Phone" ).JustInt( "", false ),
                PhoneExt       = null, //dr_GetString( "Contact_PhoneExt" ).JustInt( "", false ),
                AltPhone       = null, //kdr_GetString( "Contact_AltPhone" ).JustInt( "", false ),
                AltPhoneExt    = null, //dr_GetString( "Contact_AltPhoneExt" ).JustInt( "", false ),
                CellPhone      = null,//dr_GetString( "Contact_CellPhone" ).JustInt( "", false ),
                Pager          = null, //dr_GetString( "Contact_Pager" ).JustInt( "", false ),
                Fax            = dr_GetString( "Contact_Fax" ).JustInt( "", false ),
                Email          = dr_GetString( "Contact_Email" ),                       
                CanContact     = dr_GetString( "Contact_CanContact" ).IsTrue()

            };
            //o.AlternateContact                    = // AltContactType[]    
            o.MCRequired                          = dr_GetString( "MCRequired"            ).IsTrue();                
            o.MCRequiredSpecified                 = dr_GetString( "MCRequiredSpecified"   ).IsTrue();                
            o.IsBorrowerCC                        = dr_GetString( "IsBorrowerCC"          ).IsTrue();                
            o.IsBorrowerCCSpecified               = dr_GetString( "IsBorrowerCCSpecified" ).IsTrue();
            o.PaymentParty                        = null;
            o.ParentDocID                         = null;
            o.ByPassDupChk                        = dr_GetString( "ByPassDupChk"          ).IsTrue();                
            o.ByPassDupChkSpecified               = dr_GetString( "ByPassDupChkSpecified" ).IsTrue();                
            o.UsePreviousServiceProvider          = dr_GetString( "UsePreviousServiceProvider"          ).IsTrue();                
            o.UsePreviousServiceProviderSpecified = dr_GetString( "UsePreviousServiceProviderSpecified" ).IsTrue();                
            o.UsePreviousReviewer                 = dr_GetString( "UsePreviousReviewer"                 ).IsTrue();                
            o.UsePreviousReviewerSpecified        = dr_GetString( "UsePreviousReviewerSpecified"        ).IsTrue();                
            o.UsePreviousOrderAttributes          = dr_GetString( "UsePreviousOrderAttributes"          ).IsTrue();                
            o.UsePreviousOrderAttributesSpecified = dr_GetString( "UsePreviousOrderAttributesSpecified" ).IsTrue();                
            o.CustomerID                          = null;//dr_GetString( "CustomerID" );              
            o.BatchName                           = dr_GetString( "BatchName" );

            // following s/not happend.  all dbCols -> xmlFields s/be accounted for
            if ( nof != null && nof.Count > 0 && config.saveTempFiles )
            {
                string fname = respOrdFileName.FormatWith( DateTime.Now.ToString( ts ), orderId ).Replace( "response", "nof" );
                string tmpFile = Path.Combine( config.tempFileDir, fname );
                File.WriteAllText( tmpFile, String.Join( "\n", nof.ToArray() ) );
            }

            if ( IsOrderAnUpdate )
            {
                vendorOrderNo = dr_GetString("vendorOrderNo");
                updOrderXml = new UpdateOrderStatusRq();
                updOrderXml.Header = h;
                updOrderXml.OrderStatusUpdate = new UpdateOrderStatusRqType();
                updOrderXml.OrderStatusUpdate.DocId = dr_GetString("vendorOrderNo");
                updOrderXml.OrderStatusUpdate.StatusId = "N";

                amgr = dr_GetString( "AMFirstName", "" ).IsStrEmpty() ? "" : "{0} {1}".FormatWith( dr_GetString( "AMFirstName" ), dr_GetString( "AMLastName" ) );

                updOrderCommentXml = new AddOrderCommentRQ();
                updOrderCommentXml.Header             = h;
                updOrderCommentXml.Comment            = new OrderCommentType();
                updOrderCommentXml.Comment.Author     = amgr;
                updOrderCommentXml.Comment.PostDate   = DateTime.Parse(dr_GetString("requestModified"));
                updOrderCommentXml.Comment.Comment    = dr_GetString( "reason_for_return", "" ).Left( 4000 );
                updOrderCommentXml.Comment.ViewRights = OrderCommentTypeViewRights.ServiceProvider;
                updOrderCommentXml.Comment.Subject    = "Revisions Requested";
                updOrderCommentXml.Comment.DocId      = dr_GetString("vendorOrderNo");
            }
        }

        private string dr_GetString( string fieldName, string defaultTo = null )
        {
            fieldName = fieldName.Trim();
            if ( flds.ContainsKey( fieldName ) )
            {
                string text = dataset[ fieldName ].ToString();
                if ( text.IsStrEmpty() )
                {
                    return defaultTo;
                }                    
                return text.LeftOf( " 12:00:00 AM" );
            }
            nof.Add( "nof: " + fieldName );
            return defaultTo;
        }

        private CreateOrderRs PostOrderRequest( int doId, string fileType = "" )
        {
            string fname = respOrdFileName.FormatWith( DateTime.Now.ToString( ts ), doId + fileType );
            string responseFile = Path.Combine( config.tempFileDir, fname );

            if ( File.Exists( responseFile ) ) File.Delete( responseFile );

            string err = null;

            this.responseFile = responseFile;
            this.valId = doId;

            if ( wp.SaveFileFromUrl( responseFile, out err, PrepOrderRequest, maxFileSize: config.maxFilesize ) )
            {
                try  //success;  get tmpfile.  deserialize it
                {
                    var resp = File.ReadAllText( responseFile ).Deserialize<CreateOrderRs>();
                    if ( resp == null )
                    {
                        False( "CMS ResponseFile not recognized (not Deserialized) " + responseFile );
                    }
                    else if ( resp.Tracking != null && resp.Tracking.DocId.HasValue() )
                    {
                        //success
                        vendorOrderNo = resp.Tracking.DocId;
                        resultsLog.Add( "Order Placed. <DocId>{0}</DocId><FolderId>{1}</FolderId><LoanNumber>{2}</LoanNumber>"
                            .FormatWith( resp.Tracking.DocId, resp.Tracking.FolderId, resp.Tracking.LoanNumber ));
                    }
                    else
                        resultsLog.Add( "Order Placed. No Tracking/DocId returned from CMS" );

                    return resp;  //good return here
                }
                catch ( Exception e )
                {
                    False( "Error getting CMS ResponseFile: {0} {1} ".FormatWith( responseFile, e.Message ) );
                }
            }
            else
            {
                False( "SaveFileFormUrl download failed: {0} url: {1} saveTo: {2}".FormatWith( err, config.url, responseFile ) );
            }

            if ( ! config.saveTempFiles && File.Exists( responseFile ) ) File.Delete( responseFile );

            return null;
        }

        private HttpWebRequest PrepOrderRequest()
        {

            var request = ( HttpWebRequest ) WebRequest.Create( config.url );

            request.Method            = "POST";
            request.AllowAutoRedirect = false;
            request.Timeout           = config.requestTimeoutMs;
            request.Accept            = "application/xml";
            request.MediaType         = config.requestMediaType;
            request.ContentType       = "text/xml; encoding='utf-8'";

            string xmlContent = orderXml.SerializeObject<CreateOrderRq>();
            byte[] bytes;
            bytes = System.Text.Encoding.ASCII.GetBytes( xmlContent );

            request.ContentLength     = bytes.Length;

            Stream rs = request.GetRequestStream();
            rs.Write( bytes, 0, bytes.Length );
            rs.Close();

            // 3. save request while testing

            if ( config.saveTempFiles )
            {
                File.WriteAllText( responseFile.Replace( "response", "request" ), xmlContent );
            }

            return request;

        }

        private StandardResponse PostOrderUpdate(int doId, string fileType = "")
        {
            string fname = respOrdFileName.FormatWith(DateTime.Now.ToString(ts), doId + fileType);
            string responseFile = Path.Combine(config.tempFileDir, fname);

            if (File.Exists(responseFile)) File.Delete(responseFile);

            string err = null;

            this.responseFile = responseFile;
            this.valId = doId;

            if (wp.SaveFileFromUrl(responseFile, out err, PrepOrderUpdate, maxFileSize: config.maxFilesize))
            {
                try  //success;  get tmpfile.  deserialize it
                {
                    var resp = File.ReadAllText(responseFile).Deserialize<StandardResponse>();
                    if (resp == null)
                    {
                        False("CMS ResponseFile not recognized (not Deserialized) " + responseFile);
                    }
                    else if (resp.ResponseHdr.Code == ResponseHdrStatusType.OK)
                    {
                        //success
                        resultsLog.Add("Status updated. {0}".FormatWith(resp.ResponseHdr.Message));
                    }
                    else
                        False("Status Update Error: {0} {1}".FormatWith(resp.ResponseHdr.Code, resp.ResponseHdr.Message));

                    return resp;  //good return here
                }
                catch (Exception e)
                {
                    False("Error getting CMS ResponseFile: {0} {1} ".FormatWith(responseFile, e.Message));
                }
            }
            else
            {
                False("SaveFileFormUrl download failed: {0} url: {1} saveTo: {2}".FormatWith(err, config.url, responseFile));
            }

            if (!config.saveTempFiles && File.Exists(responseFile)) File.Delete(responseFile);

            return null;
        }

        private AddOrderCommentRS PostOrderComment(int doId, string fileType = "")
        {
            string fname = respOrdFileName.FormatWith(DateTime.Now.ToString(ts), doId + fileType);
            string responseFile = Path.Combine(config.tempFileDir, fname);

            if (File.Exists(responseFile)) File.Delete(responseFile);

            string err = null;

            this.responseFile = responseFile;
            this.valId = doId;

            if (wp.SaveFileFromUrl(responseFile, out err, PrepOrderComment, maxFileSize: config.maxFilesize))
            {
                try  //success;  get tmpfile.  deserialize it
                {
                    var resp = File.ReadAllText(responseFile).Deserialize<AddOrderCommentRS>();
                    if (resp == null)
                    {
                        False("CMS ResponseFile not recognized (not Deserialized) " + responseFile);
                    }
                    else if (resp.Header.Code == ResponseHdrStatusType.OK)
                    {
                        //success
                        resultsLog.Add("Comment Sent. Comment Id: {0}".FormatWith(resp.CommentId));
                    }
                    else
                        False("Post Comment Error: {0} {1}".FormatWith(resp.Header.Code, resp.Header.Message));

                    return resp;  //good return here
                }
                catch (Exception e)
                {
                    False("Error getting CMS ResponseFile: {0} {1} ".FormatWith(responseFile, e.Message));
                }
            }
            else
            {
                False("SaveFileFormUrl download failed: {0} url: {1} saveTo: {2}".FormatWith(err, config.url, responseFile));
            }

            if (!config.saveTempFiles && File.Exists(responseFile)) File.Delete(responseFile);

            return null;
        }

        private HttpWebRequest PrepOrderUpdate()
        {
            var request = (HttpWebRequest)WebRequest.Create(config.url);

            request.Method = "POST";
            request.AllowAutoRedirect = false;
            request.Timeout = config.requestTimeoutMs;
            request.Accept = "application/xml";
            request.MediaType = config.requestMediaType;
            request.ContentType = "text/xml; encoding='utf-8'";

            string xmlContent = updOrderXml.SerializeObject<UpdateOrderStatusRq>();
            byte[] bytes;
            bytes = System.Text.Encoding.ASCII.GetBytes(xmlContent);

            request.ContentLength = bytes.Length;

            Stream rs = request.GetRequestStream();
            rs.Write(bytes, 0, bytes.Length);
            rs.Close();

            // 3. save request while testing

            if (config.saveTempFiles)
            {
                File.WriteAllText(responseFile.Replace("response", "request"), xmlContent);
            }

            return request;
        }

        private HttpWebRequest PrepOrderComment()
        {
            var request = (HttpWebRequest)WebRequest.Create(config.url);

            request.Method = "POST";
            request.AllowAutoRedirect = false;
            request.Timeout = config.requestTimeoutMs;
            request.Accept = "application/xml";
            request.MediaType = config.requestMediaType;
            request.ContentType = "text/xml; encoding='utf-8'";

            string xmlContent = updOrderCommentXml.SerializeObject<AddOrderCommentRQ>();
            byte[] bytes;
            bytes = System.Text.Encoding.ASCII.GetBytes(xmlContent);

            request.ContentLength = bytes.Length;

            Stream rs = request.GetRequestStream();
            rs.Write(bytes, 0, bytes.Length);
            rs.Close();

            // 3. save request while testing

            if (config.saveTempFiles)
            {
                File.WriteAllText(responseFile.Replace("response", "request"), xmlContent);
            }

            return request;
        }

        private bool UploadEnvFile( int orderId, string envFilePath = null )
        {
            if ( envFilePath == null )
                envFP = Path.Combine( config.saveEnvDir, envFileName );
            else
            {
                envFP = envFilePath;
                envFileName = Path.GetFileName( envFilePath );
            }               

            if ( envFileName.IsStrEmpty() )
            {
                return False( "Bpo_Task_Id not DONE on PreMarketing Tab" );
            }
            else if ( orderResponse.Tracking == null || orderResponse.Tracking.DocId.IsStrEmpty() )
            {
                return False( "CMS did not return Tracking info (documentId)." );
            }
            else if ( ! File.Exists( envFP ) )
            {
                return False( "Env File not found: {0}".FormatWith( envFileName ) );
            }

            string fname = respEnvFileName.FormatWith( DateTime.Now.ToString( ts ), orderId );
            string responseFile = Path.Combine( config.tempFileDir, fname );

            if ( File.Exists( responseFile ) ) File.Delete( responseFile );

            string err        = null;
            this.valId        = orderId;
            this.responseFile = responseFile;

            if ( wp.SaveFileFromUrl( responseFile, out err, PrepUploadEnvFile, maxFileSize: config.maxFilesize ) )
            {
                try  //success;  get tmpfile.  deserialize it
                {
                    var resp = File.ReadAllText( responseFile ).Deserialize<ResNet.CMS.Orders.StandardResponse>();
                    if ( resp == null )
                    {
                        False( "CMS Env UploadFileRq not returned" );
                    }
                    else if ( resp.ResponseHdr.Code == ResponseHdrStatusType.OK )
                    {
                        resultsLog.Add( ".ENV file uploaded OK: {0}".FormatWith( envFileName ) );
                    }
                    else
                        False( ".ENV File upload Error: {0} {1}".FormatWith( resp.ResponseHdr.Code, resp.ResponseHdr.Message) );
                }
                catch ( Exception e )
                {
                    False( "CMS Env Upload Error: " + e.Message );
                }
            }
            else
            {
                False( "SaveFileFormUrl download failed: {0} url: {1} saveTo: {2}".FormatWith( err, config.url, responseFile ) );
            }

            if ( ! config.saveTempFiles && File.Exists( responseFile ) ) File.Delete( responseFile );

            return true;
        }

        private HttpWebRequest PrepUploadEnvFile()
        {
            string docId = orderResponse.Tracking.DocId.Trim();
            var uplReq = new UploadFileRq()
            {
                Header = envOrderHeader,
                Tracking = new UploadFileType()
                {
                    DocId = docId,
                    FileName = envFileName,
                    FileTypeId = "BPO"
                }
            };

            uplReq.Header.CorrelationId = null;
            uplReq.Header.SequenceKey = null;

            var xml = uplReq.SerializeObject<UploadFileRq>();
            // 1. save request while testing

            var postData = new HttpPostMimeParts();
            postData.AddPart( xml
                    , "Content-Disposition: form-data; name=\"payload\""
                    , "Content-Type: application/xml"
                );
            try
            {
                var file = new FileStream( envFP, FileMode.Open );
                byte[] fileData = new byte[ file.Length ];
                file.Read( fileData, 0, fileData.Length );

                postData.AddPart( fileData
                    , "Content-Disposition: form-data; name=\"file1\"; filename=\"{0}\"".FormatWith( envFileName )
                    , "Content-Type: application/octet-stream"
                    );

                file.Close();
            }
            catch ( Exception e )
            {
                False( "Can't read EnvFile: {0}, {1}".FormatWith( envFileName, e.Message ) );
                return null;
            }

            // 2. setup webRequest

            var request = ( HttpWebRequest ) WebRequest.Create( config.fileUrl );

            request.Method            = "POST";
            request.KeepAlive         = true;
            request.ContentLength     = postData.ContentLength;
            request.Accept            = config.requestAccept;
            request.AllowAutoRedirect = false;
            request.Timeout           = config.requestTimeoutMs;
            request.MediaType         = config.requestMediaType;
            request.ContentType       = "multipart/form-data: boundary=" + postData.Boundary;

            postData.SetStream( request );

            // 3. save request while testing

            if ( config.saveTempFiles )
            {
                postData.LogRequestFile( responseFile.Replace( "response", "request" ) );
            }

            return request;

        }
        
    }
}
