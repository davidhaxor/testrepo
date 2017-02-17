using Devart.Data.MySql;
using GenProcs.MyDbl;
using GenProcs.Utils;
using log4net;
using ResNet.Bpo.CMS;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using ResNet.CMS.Orders;
using GenProcs;

namespace MyServices
{
    public class SendToCMS
    {
        public enum RunType { service, console, runOnce }

        public class Config
        {
            public RunType runType = RunType.service;
            public string listenPath = @"RepIf";
            public string listenFilter = "*.txt";
            public int timerInterval = 1000 * 60 * 5; //5 mins
            public int sleepBefore = 100;
            public string fileRefreshTrigger = "refresh_config";
            public int transPauseMs = 0;
        }

        public string ValuationId;
        public Config config;
        public bool stopSignaled;

        private static readonly log4net.ILog log = LogManager.GetLogger( typeof( SendToCMS ) );
        private enum CodeTypes { eventLoc, memberLoc, calendarLoc }
        private System.Timers.Timer timer1;
        private FileSystemWatcher watcher;
        private StringBuilder errs;
        private string sqlGetBpoInfo;
        private string sqlGetBpoPics;
        private string sqlGetOrderInfo;
        private string sqlGetBpoEnvName;
        private Dictionary<string, string> dictConfig;
        private Dictionary<string, int> filesAvoid;  //Add removeFromList function to retry
        private const int refreshId = -999;
        private string prefixEnvPdf = "";
        private string prefixOrderApr = "";
        private string prefixOrderBpo = "";
        private string lastErr;
        private const string noData = "GetOrderInfo did not return Order Data. doId: ";
        private const string noRead = "DbRead failed for doId: ";

        #region SqlStmts

        private string selConfigParms =
"select distinct lower(pkey) pkey, descr from resnet_if.parms where ptype='SendToCMSEnv' and res_id=0";
        private const string selParmsSql =
"select `sql` from resnet_if.parms_sql where ptype='{0}' and pkey='{1}' and res_id=0";

        private const string updProcRepEntry =
"update resnet.proc_reps set status='{0}', report_parms=concat(report_parms,'{1}') " +
"where report_name='{2}' and status='InProcess' and task_id={3} and (property_id={4} or ({4}=0 and status_dt > date_sub(now(), interval 10 minute)))";

        private const string updOrderRequest =
"update resnet_if.bpo_requests " +
"set sent=now(), status='{0}', status_msg=if('{4}'>'','{4}',status_msg), " +
"vendor_order_no='{1}', log=trim('\n' from trim(concat(now(), '{2}', '\n\n', ifnull(trim('\n' from trim(log)),'')))) " + 
"where bpo_request_id='{3}' and vendor='CMS'";

        private const string addEnvOrderRequest =
@"insert into resnet_if.bpo_requests (
 ordered, sent, status, due, vendor
, vendor_order_no, completed, property_id, task_id, res_id
, address, city, state, zip, loan_no
, borrower_last, borrower_first, document_type, document_name, status_msg
, file, bpo_type, bpo_date
) values ( 
 now(), now(), 'done', now(), '{0}'
, '{1}', now(), {2}, {3}, {4}
, '{5}', '{6}', '{7}', '{8}', '{9}'
, '{10}', '{11}', '{12}', '{13}', '{14}'
, '{15}', '{16}', now()
);";

        private const string selEnvParentDocId = @"select vendor_order_no 
from resnet_if.bpo_requests 
where vendor='{0}' and property_id={1} and document_type='env' and bpo_type='102'
order by 1 desc limit 1";

        private const string selEvnSentUnion = "select count(*) from( {0} union {1} ) x where knt>0";
        private const string selEvnSent = "select count(*) knt" +
" from resnet_if.bpo_requests val" +
" where val.vendor= 'CMS'" +
" and val.status in ('new','open','done')" +
" and val.loan_no='{0}'" +
" and status_msg like '%env%file%sent%'";

        #endregion

        public SendToCMS()
        {
            config = new Config();
        }

        public void Start()
        {
            if ( !SetConfig( "Started" ) )
            {
                log.ErrorFormat( "Missing config: " + errs.ToString() );
                stopSignaled = true;
                return;
            }

            stopSignaled = false;

            watcher = new FileSystemWatcher();
            watcher.Path = config.listenPath;
            watcher.Filter = config.listenFilter;
            watcher.IncludeSubdirectories = false;
            watcher.NotifyFilter = NotifyFilters.LastAccess
                | NotifyFilters.LastWrite
                | NotifyFilters.FileName
                | NotifyFilters.DirectoryName;
            watcher.Changed += new FileSystemEventHandler( OnChanged );
            watcher.Created += new FileSystemEventHandler( OnChanged );
            watcher.Deleted += new FileSystemEventHandler( OnChanged );
            watcher.Renamed += new RenamedEventHandler( OnRenamed );

            timer1 = new System.Timers.Timer();
            timer1.Elapsed += new System.Timers.ElapsedEventHandler( timer1_Elapsed );
            timer1.Interval = config.timerInterval;

            //Good to go
            timer1.Enabled = true;
            watcher.EnableRaisingEvents = true;

            DoProcess();
        }

        public void Stop()
        {
            if ( timer1 != null ) timer1.Enabled = false;
            stopSignaled = true;
            log.Info( config.runType.ToString() + " Stopped" );
            Db.Instance.Close();
        }

        public string PingDb()
        {

            Db.Instance.Open();  //Instance Creation opens db, Open() does a ping to see if open, if not opens it
            return "Database DateTime: " + Db.Instance.SqlFunc( "select now()" );
        }

        public void RunOnce()
        {
            if ( !SetConfigItems() )
            {
                log.ErrorFormat( "Missing Config Entries: " + errs.ToString() );
                return;
            }
            log.Info( "{0} {1}: {2}".FormatWith( config.runType, "Vid", ValuationId ) );

            int id = ValuationId.RightOfR( "_" ).LeftOf( "." ).ToIntDef( 0 );

            if      ( ValuationId.StartsWith( prefixEnvPdf   ) ) DoBpo2PdfEnv( id );
            else if ( ValuationId.StartsWith( prefixOrderApr ) )
                DoCmsOrder( prefixOrderApr, id );
            else if ( ValuationId.StartsWith( prefixOrderBpo ) )
                DoCmsOrder( prefixOrderBpo, id);
            else
                log.ErrorFormat( "Unknown ValId: " + ValuationId );
        }

        private bool SetConfig( string msg )
        {
            if ( !SetConfigItems() ) return false;

            log.Info( "runType: {0} listenPath: {1} listenFilter: {2} timerInterval: {3} {4}".FormatWith(
                      config.runType
                    , config.listenPath
                    , config.listenFilter
                    , config.timerInterval
                    , msg
                    ) );

            return true;
        }

        private bool SetConfigItems()
        {
            filesAvoid = new Dictionary<string, int>( 200, StringComparer.InvariantCultureIgnoreCase );
            Db.Instance.Open();
            errs = new StringBuilder();
            config = new Config();

            try
            {
                string srce = System.Configuration.ConfigurationManager.AppSettings[ "configValues" ].ToStringDef( "" );
                if ( srce.ToLower() == "resnet.parms" )
                {
                    dictConfig = Db.Instance.InitializeDefaults( String.Format( selConfigParms ) );
                }
                else
                {
                    var aa = System.Configuration.ConfigurationManager.AppSettings;
                    dictConfig = aa
                        .Cast<string>()
                        .ToDictionary( p => p, p => aa[ p ], StringComparer.InvariantCultureIgnoreCase );
                }
                if ( dictConfig == null || dictConfig.Count == 0 )
                {
                    errs.Append( "No Parms entries in resnet_if.parms_sql" );
                    return false;
                }

                config.listenFilter       = dictConfig.GetValueDef( "listen_FILter" );
                config.listenPath         = dictConfig.GetValueDef( "listen_path" );
                config.sleepBefore        = dictConfig.GetValueDef( "sleep_before_ms" ).ToIntDef( config.sleepBefore );
                config.timerInterval      = dictConfig.GetValueDef( "timer_interval_mins" ).ToIntDef( 5 ) * 60 * 1000; //mins
                config.transPauseMs       = dictConfig.GetValueDef( "PauseMsBetweenTransactions" ).ToStringDef( "" ).ToIntDef( 0 );
                config.fileRefreshTrigger = dictConfig.GetValueDef( "refresh_config_trigger" ).ToStringDef( config.fileRefreshTrigger ).ToLower();
                config.fileRefreshTrigger = Path.GetFileNameWithoutExtension( config.fileRefreshTrigger );
                prefixEnvPdf              = dictConfig.GetValueDef( "envFilePrefix" ).ToStringDef( "" ).ToLower();
                prefixOrderApr            = dictConfig.GetValueDef( "cmsFilePrefixApr" ).ToStringDef( "" ).ToLower();
                prefixOrderBpo            = dictConfig.GetValueDef( "cmsFilePrefixBpo" ).ToStringDef( "" ).ToLower();

                sqlGetBpoInfo    = Db.Instance.GetDbSql( selParmsSql.FormatWith( "SendToCMSEnv", "Bpo2Env" ), true, true );
                sqlGetBpoPics    = Db.Instance.GetDbSql( selParmsSql.FormatWith( "SendToCMSEnv", "Bpo2EnvPics" ), true, true );
                sqlGetOrderInfo  = Db.Instance.GetDbSql( selParmsSql.FormatWith( "SendToCMSOrder", "ValOrderInfo" ), true, true );
                sqlGetBpoEnvName = Db.Instance.GetDbSql( selParmsSql.FormatWith( "SendToCMSOrder", "EnvFileName" ), true, true );

                if ( config.listenFilter.IsStrEmpty()  ) errs.Append( ", listen_filter" );
                if ( config.listenPath.IsStrEmpty()    ) errs.Append( ", listen_path" );
                if ( prefixEnvPdf.IsStrEmpty()         ) errs.Append( ", envFilePrefix" );
                if ( prefixOrderApr.IsStrEmpty()       ) errs.Append( ", cmsFilePrefixApr" );
                if ( prefixOrderBpo.IsStrEmpty()       ) errs.Append( ", cmsFilePrefixBpo" );
                if ( sqlGetBpoInfo.IsStrEmpty()        ) errs.Append( ", SendToCMSEnv/Bpo2Env ParmsSql" );
                if ( sqlGetBpoPics.IsStrEmpty()        ) errs.Append( ", SendToCMSEnv/Bpo2EnvPics ParmsSql" );
                if ( sqlGetOrderInfo.IsStrEmpty()      ) errs.Append( ", SendToCMSOrder/ValOrderInfo ParmsSql" );
                if ( sqlGetBpoEnvName.IsStrEmpty()     ) errs.Append( ", SendToCMSOrder/EnvFileName ParmsSql" );

                
                if ( ! Directory.Exists( config.listenPath ) ) errs.Append( ", Invalid listenPath: " + config.listenPath );

                if ( errs.Length > 0 )
                {
                    errs.Insert( 0, "Required Config Values missing: " + errs.ToString().Substring( 1 ) );
                }

                foreach ( var item in dictConfig )
                {
                    if ( item.Key.EndsWith( "dir", StringComparison.InvariantCultureIgnoreCase ) )
                        if ( ! Directory.Exists( item.Value ) )
                            errs.Append( ", " + item.Key );
                }

                if ( errs.Length > 0 )
                {
                    errs.Insert( 0, "Required Config Paths are Invalid: " + errs.ToString().Substring( 1 ) );
                }

                if ( errs.Length > 0 ) return false;

                errs.Append( GetBpoPdfEnv.CheckConfig( dictConfig ) );

                if ( errs.Length > 0 ) return false;

                errs.Append( CmsOrder.CheckConfig( dictConfig ) );

                if ( errs.Length > 0 ) return false;

                    return errs.Length == 0;
            }
            catch ( Exception e )
            {
                errs.Append( "SetDbConfig " + e.Message );
                return false;
            }
            finally
            {
                if ( errs.Length > 0 ) Db.Instance.Close();  //exiting
            }
        }

        private void timer1_Elapsed( object sender, System.Timers.ElapsedEventArgs e )
        {
            DoProcess();
        }

        public void OnChanged( object source, FileSystemEventArgs e )
        {
            DoProcess();
        }

        public void OnRenamed( object source, RenamedEventArgs e )
        {
            DoProcess();
        }

        private void DoProcess()
        {
            watcher.EnableRaisingEvents = false;
            timer1.Enabled = false;
            System.Threading.Thread.Sleep( config.sleepBefore );

            DoFiles();

            timer1.Enabled = true;
            watcher.EnableRaisingEvents = true;
        }

        private void DoFiles()
        {
            try
            {
                while ( true )
                {
                    if ( stopSignaled ) break;

                    string err = "";
                    var arrFiles = Directory.GetFiles( watcher.Path, watcher.Filter )
                                    .Where( w => !filesAvoid.ContainsKey( Path.GetFileName( w ) ) )
                                    .ToArray();

                    if ( arrFiles.Length == 0 ) break;

                    foreach ( string filePath in arrFiles )
                    {
                        if ( stopSignaled ) break;
                        if ( filesAvoid.ContainsKey( Path.GetFileName( filePath ) ) ) continue;
                        try
                        {
                            int valId = GetTid( filePath, out err );
                            string fName = Path.GetFileName( filePath ).ToLower();

                            if ( ! DeleteFile( filePath ) )
                            {
                                Thread.Sleep( 100 );
                                DeleteFile( filePath );
                            }
                            if ( valId == -1 )
                            {
                                log.ErrorFormat( "Error: Unrecognized file: {0} {1}", filePath, err );
                            }
                            else if ( valId == refreshId )
                            {
                                SetConfig( "Refresh" );
                                break;
                            }
                            else if ( fName.StartsWith( prefixEnvPdf ) )
                            {
                                DoBpo2PdfEnv( valId );
                            }
                            else if ( fName.StartsWith( prefixOrderApr ) )
                            {
                                DoCmsOrder( prefixOrderApr, valId );
                            }
                            else if ( fName.StartsWith( prefixOrderBpo ) )
                            {
                                DoCmsOrder( prefixOrderBpo, valId );
                            }
                            else
                            {
                                log.ErrorFormat( "Error: Unrecognized filename prefix: {0}", filePath );
                            }

                            if ( stopSignaled ) break;

                        }
                        catch ( Exception ex )
                        {
                            log.ErrorFormat( "Error: {0} {1}", filePath, ex.Message );
                            DeleteFile( filePath );
                        }
                    } //for

                } //while

            }
            finally
            {
                Db.Instance.Close();
            }
        }

        private bool DeleteFile( string filePath )
        {
            try
            {
                if ( File.Exists( filePath ) ) File.Delete( filePath );
                return true;
            }
            catch
            {
                //string fn = Path.GetFileName( filePath );  //decided to see how this behaves in prod first
                //if ( filesAvoid.ContainsKey( fn ) )
                //    filesAvoid[ filePath ]++;
                //else 
                //    filesAvoid.Add( fn, 0 );
                return false;
            }
        }

        private int GetTid( string filePath, out string errMsg )
        {
            errMsg = "";
            try
            {
                FileInfo fi = new FileInfo( filePath );
                if ( fi == null )
                {
                    errMsg = "can't get FileInfo";
                    return -1;
                }
                string fn = Path.GetFileNameWithoutExtension( filePath ).ToLower();

                if ( fn == config.fileRefreshTrigger )
                {
                    return refreshId;
                }
                return fn.RightOfR( "_" ).ToIntDef( -1 ); // Prefix_YYYYMMDD_HHNNSS_###tid##
            }
            catch ( Exception e )
            {
                errMsg = e.Message;
                return -1;
            }
        }

        private bool False( string msg )
        {
            StackFrame frame = new StackFrame( 1 );
            var method = frame.GetMethod();
            var type = method.DeclaringType;
            var name = method.Name;

            log.ErrorFormat( name + ": " + msg );
            lastErr = msg;
            return false;
        }

        public bool DoBpo2PdfEnv( int doId )
        {
            var sw = Stopwatch.StartNew();

            Db.Instance.Open();  //only opens 1 time. subsequent opens just ping the db

            MySqlDataReader dr = null;
            MySqlDataReader drPics = null;
            int picKnt = 0;
            string err = "";
            string pid = "";
            string taskType = "";

            try
            {
                // 1. get bpo pics list

                string sql = sqlGetBpoPics.FormatWith( doId );

                if ( !Db.Instance.ExecReader( sql, ref drPics, ref err ) )
                {
                    err = err.ToStringDef( "GetBpoPics did not return Bpo Pic Data. Tid: " + doId.ToString() );
                    return False( err );
                }

                // 2. get bpo data
                // run-time args: {0} is property_id {1} is pic task_id {2} is picTaskType (leave comment here)

                pid      = drPics[ "property_id" ].ToString();
                taskType = drPics[ "task_type" ].ToString();

                sql = sqlGetBpoInfo.FormatWith( pid, doId, taskType );

                if ( !Db.Instance.ExecReader( sql, ref dr, ref err ) )
                {
                    err = err.ToStringDef( "GetBpoInfo did not return BpoData. Tid: " + doId.ToString() );
                    return False( err );
                }
                if ( ! dr.Read() )
                {
                    err = "GetBpoInfo DbRead failed for Tid: " + doId.ToString();
                    return False( err );
                }

                // 3. Generate BPO Pdf and Env files at FNC site and download files

                var bpo = new GetBpoPdfEnv( dictConfig );
                bpo.pid = pid;
                bpo.pdfOnly = taskType.ToLower() == "bpoupic";

                if ( !bpo.Execute( doId, dr, drPics ) )
                {
                    picKnt = bpo.picCount;
                    err = bpo.errorMsg + " Tid: " + doId.ToString();
                    return False( err );
                }

                picKnt = bpo.picCount;
                err = "";

                if ( bpo.pdfOnly ) return true;

                // 4. send Env File to CMS System as a CMS Order, Save DocId to bpo_orders table

                var order = new CmsOrder( dictConfig );

                if ( !order.SendEnvAsOrder( doId, dr, bpo.fileEnvPath ) )
                {
                    return False( order.errorMsg + " doId: " + doId.ToString() );
                }

                if ( order.envDocId.HasValue() )
                {
                    Db.Instance.SqlExec( addEnvOrderRequest.FormatWith(  //set up Bpo_Order as Done status
                          "CMS"          // '{0}'  char( 10 )   vendor
                        , order.envDocId // '{1}'  char( 25 )   vendor_order_no
                        , pid           //{2} mediumint( 7 ) unsigned property_id
                        , doId          // {3} int( 10 )      unsigned task_id
                        , dr[ "res_id" ].ToString()             //{ 4} mediumint( 6 ) res_id
                        , dr[ "subj_addr_street" ].ToString()   // '{5}'  char( 40 )   address
                        , dr[ "subj_addr_city" ].ToString()     // '{6}'  char( 40 )   city
                        , dr[ "subj_addr_stateprov" ].ToString()// '{7}'  char( 2 )    state
                        , dr[ "subj_addr_zip" ].ToString()      // '{8}'  char( 10 )   zip
                        , dr[ "subj_loannum" ].ToString()       // '{9}'  char( 20 )   loan_no
                        , dr[ "ApplicantLastName" ].ToString()  // '{10}' char( 40 )   borrower_last
                        , dr[ "ApplicantFirstName" ].ToString() // '{11}' char( 40 )   borrower_first
                        , "env"                 // '{12}' char( 70 )   document_type
                        , ""                    // '{13}' char( 70 )   document_name
                        , "Env File Uploaded"   // '{14}' char( 255 )  status_msg
                        , ""                    // '{15}' char( 50 )   file
                        , "102"                 // '{16}' char( 20 )   bpo_type
                    ) );
                }

                return true;
            }
            catch ( Exception e )
            {
                return False( e.Message );
            }
            finally
            {
                if ( dr != null ) dr.Close();
                if ( drPics != null ) drPics.Close();
                dr = null;
                drPics = null;

                sw.Stop();
                log.DebugFormat( "BpoTime taken ({1} pics): {0}ms", sw.Elapsed.TotalMilliseconds, picKnt );
                Db.Instance.SqlExec( updProcRepEntry.FormatWith(
                      err.IsStrEmpty() ? "Done" : "Canceled"  //status
                    , err.IsStrEmpty() ? "" : " error: " + err.Replace( "'", "" ) //report_parms
                    , "AmGetBpoPdf"
                    , doId //task_id
                    , pid.ToIntDef( 0 )
                    ) );
            }
        }

        private bool DoCmsOrder( string orderType, int doId )
        {
            var sw = Stopwatch.StartNew();

            Db.Instance.Open(); 

            MySqlDataReader dr   = null;
            bool IsApr           = orderType == prefixOrderApr;
            string envFileMsg    = "";
            string resultsMsg    = "";
            lastErr              = "";
            string vendorOrderNo = "";
            var order            = new CmsOrder( dictConfig );

            try
            {
                string sql = sqlGetOrderInfo.FormatWith( doId );  //bpo_ is the default in sql Stmt

                if ( IsApr ) sql = sql.ReplaceStr( "bpo_", "apr_" );

                // 1. get order data

                if ( ! Db.Instance.ExecReader( sql, ref dr, ref lastErr ) )
                {
                    return False( lastErr.ToStringDef( noData + doId.ToString() ) );
                }

                if ( ! dr.Read() )
                {
                    return False( noRead + doId.ToString() );
                }

                // 2. Get Bpo Env file name set as Parent Doc Id for all orders sent to CMS by property_id
                string pid = dr[ "property_id" ].ToString();

                order.sendParentDocId = Db.Instance.SqlFunc( selEnvParentDocId.FormatWith(
                     "CMS",           //vendor = '{0}' 
                     pid.ToIntDef(0) //{ 1}
                    ) );

                // 3. execute the order

                if ( ! order.Execute( doId, dr, IsApr ) )
                {
                    False( order.errorMsg + " doId: " + doId.ToString() );
                }

                resultsMsg    = String.Join( "\n", order.resultsLog.ToArray() );
                vendorOrderNo = order.vendorOrderNo;
                // see above envFileMsg    = order.envFileWasSent ? "Env File Sent " + DateTime.Now.ToString() : "";

                return lastErr.IsStrEmpty();
            }
            catch ( Exception e )
            {
                return False( e.Message );
            }
            finally
            {
                if ( dr != null ) dr.Close();
                dr = null;
                sw.Stop();
                log.DebugFormat( "OrderTime taken: {0}ms", sw.Elapsed.TotalMilliseconds );
                string updSql = updOrderRequest;

                if ( IsApr ) updSql = updSql.ReplaceStr( "bpo_", "apr_" );
                if ( lastErr.HasValue() ) lastErr = " error: " + lastErr;
                Db.Instance.SqlExec( updSql.FormatWith(
                      (vendorOrderNo.HasValue() ? "open" : "cancelled" )
                    , DbUtils.Escape( vendorOrderNo ) //folderId
                    , DbUtils.Escape( " SendOrder: {0}{1}".FormatWith(resultsMsg, lastErr) )
                    , doId //order_id
                    , DbUtils.Escape( envFileMsg ) //status_msg
                    ) );
            }
        }

    } //class
}
