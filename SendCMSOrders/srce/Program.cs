using System;
using System.Collections;
using System.ComponentModel;
using System.Configuration;
using System.Configuration.Install;
using System.Linq;
using System.ServiceProcess;
using log4net;
using GenProcs.Utils;

namespace MyServices
{
    static class Program
    {
        private static readonly log4net.ILog log = LogManager.GetLogger( typeof( Program ) );

        private static readonly string[] arrInstall   = { "-i", "-install", "--install" };
        private static readonly string[] arrUnInstall = { "-u", "-uninstall", "--uninstall" };
        private static readonly string[] arrSvcStart  = { "-s", "-startsvc", "--startsvc" };
        private static readonly string[] arrConsole   = { "-c", "-console", "--console" };
        private static readonly string[] arrOnce      = { "-o", "-once", "--once" };
        private static readonly string[] arrBpoEnv    = { "-t", "-tid", "--tid" }; 
        private static readonly string[] arrPingDb    = { "-p", "-pingdb", "--pingdb" };

        private const string help = @"
SendCMSOrders Background Monitor

command line arguments (case insensitive)

-i,-install,--install       install as Service
-u,-uninstall,--uninstall   un-install Service
-s,-startsvc,--startsvc     start service
-c,-console,--console       run at console in a loop
-o,-once,--once             run one time to process pending files in listening folder
-t=##,-tid=##,--tid=###     run for this picTaskId --> property_id
-pingDb                     test - start program, open db connection, ping db, exit
";

        static void Main( string[] args )
        {
            args = args.Select( s => s.ToLowerInvariant() ).ToArray();

            log4net.Config.XmlConfigurator.Configure();

            if ( ArgsContain( args, arrPingDb ) )
            {
                var app = new SendToCMS();
                Console.WriteLine( app.PingDb() );
            }
            else if ( ArgsContain( args, arrUnInstall ) )
            {
                Install( args, installIt: false );
            }
            else if ( ArgsContain( args, arrInstall ) )
            {
                Install( args );
            }
            else if ( ArgsContain( args, arrOnce ) )
            {
                var app = new SendToCMS();
                app.config.runType = SendToCMS.RunType.runOnce;
                app.RunOnce();
            }
            else if ( ArgsContain( args, arrBpoEnv, true ) )
            {
                var app = new SendToCMS();
                app.config.runType = SendToCMS.RunType.runOnce;
                app.ValuationId = GetArgValue( args, arrBpoEnv );
                app.RunOnce();
            }
            else if ( ArgsContain( args, arrConsole ) )
            {
                Console.WriteLine( "press 'q' to quit." );
                var app = new SendToCMS();
                app.config.runType = SendToCMS.RunType.console;
                app.Start();
                if ( ! app.stopSignaled )
                    while ( Console.ReadKey().KeyChar != 'q' )
                    {
                    }
                app.Stop();
            }
            else if ( ArgsContain( args, arrSvcStart ) )
            {
                ServiceBase[] ServicesToRun;
                ServicesToRun = new ServiceBase[]
                {
                    new Service1()
                };
                ServiceBase.Run( ServicesToRun );
            }
            else
            {
                Console.WriteLine( help );
            }
        }

        private static bool ArgsContain( string[] args, string[] switches, bool isKeyValue = false  )
        {
            return null != switches.FirstOrDefault( args.Select( s => isKeyValue ? s.LeftOf( "=" ) : s ).Contains );
        }

        private static string GetArgValue( string[] args, string[] switches )
        {
            return args.Where( a => switches.Any( s => a.StartsWith( s ) ) ).FirstOrDefault().RightOf( "=" );
        }

        static void Install( string[] args, bool installIt = true )
        {
            try
            {
                Console.WriteLine( "" ); //just a CR
                using ( AssemblyInstaller installSvc = new AssemblyInstaller( typeof( Program ).Assembly, args ) )
                {
                    IDictionary state = new Hashtable();
                    installSvc.UseNewContext = true;
                    try
                    {
                        if ( installIt )
                        {
                            installSvc.Install( state );
                            installSvc.Commit( state );
                            Console.WriteLine( ConfigurationManager.AppSettings[ "ServiceAfterInstallMsg" ] );
                        }
                        else
                        {
                            installSvc.Uninstall( state );
                        }
                    }
                    catch
                    {
                        try
                        {
                            installSvc.Rollback( state );
                        }
                        catch (Exception e )
                        {
                            log.ErrorFormat( "Error during {0}Install: {1}", ( installIt ? "" : "un-" ), e.Message );
                        }
                        throw;
                    }
                }
            }
            catch ( Exception ex )
            {
                Console.Error.WriteLine( ex.Message );
            }
        }

    } //class Program

    [RunInstaller( true )]
    public sealed class MyServiceInstallerProcess : ServiceProcessInstaller
    {
        public MyServiceInstallerProcess()
        {
            this.Username = ConfigurationManager.AppSettings[ "ServiceUserName" ];
            this.Password = ConfigurationManager.AppSettings[ "ServicePassword" ];
            string s = ConfigurationManager.AppSettings[ "ServiceAccount" ];
            if ( Enum.IsDefined( typeof( ServiceAccount ), s ) )
                this.Account = ( ServiceAccount ) Enum.Parse( typeof( ServiceAccount ), s, true );
            else
                this.Account = ServiceAccount.User;

        }
    }

    [RunInstaller( true )]
    public sealed class MyServiceInstaller : ServiceInstaller
    {
        public MyServiceInstaller()
        {
            this.Description = ConfigurationManager.AppSettings[ "ServiceDescription" ];
            this.DisplayName = ConfigurationManager.AppSettings[ "ServiceDisplayName" ];
            this.ServiceName = ConfigurationManager.AppSettings[ "ServiceName" ];
            this.StartType = System.ServiceProcess.ServiceStartMode.Automatic;
        }
    }

}
