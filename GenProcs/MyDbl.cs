using System;
using System.Configuration;
using System.Collections.Generic;
using System.Text;
using System.Globalization;
using Devart.Data.MySql;
using log4net;
using GenProcs.Utils;

namespace GenProcs.MyDbl
{
    public sealed class Db
    {
        private log4net.ILog log = log4net.LogManager.GetLogger( typeof(Db) );

        private string connectionString;
        private MySqlConnection dbConnNoPool;
        private System.Data.CommandBehavior readerCmdBehavior;
        private bool pooled;


        private static volatile Db _instance; //singleton pattern - properties
        private static object syncRoot = new Object();
        private Db() { }                      //singleton pattern - hide constructor
        public static Db Instance             //singleton pattern - access 
        {
            get
            {
                if ( _instance == null )
                {
                    lock ( syncRoot )
                    {
                        if ( _instance == null ) _instance = new Db();
                    }
                }
                return _instance;
            }
        }

        static Db()
        {
            Instance.dbConnNoPool = null;
            string connStr = ConfigurationManager.AppSettings[ "DbConnectionString" ].ToStringDef( "MySQLResnet" );
            Instance.connectionString = ConfigurationManager.ConnectionStrings[ connStr ].ConnectionString;
            Instance.pooled = Instance.connectionString.Replace( " ", "" ).IndexOf( "Pooling=true", StringComparison.OrdinalIgnoreCase ) > -1;

            if ( Instance.pooled )
                Instance.readerCmdBehavior = System.Data.CommandBehavior.CloseConnection;  //for pooling            
            else 
            {
                try
                {
                    Instance.readerCmdBehavior = System.Data.CommandBehavior.Default;  /// for No pooling
                    Instance.dbConnNoPool = new MySqlConnection( Instance.connectionString );
                    Instance.dbConnNoPool.Open();  //?? trying out try/catch - jc
                }
                catch ( Exception e )
                {
                    Instance.log.Error( "NoPooling error" + e.Message );
                }                
            }
        }

        public string GetConnectionString()
        {
            return connectionString;
        }

        private MySqlConnection GetConn()
        {
            if ( pooled )
            {
                var dbConn = new MySqlConnection( GetConnectionString() );
                dbConn.Open();

                if ( !dbConn.Ping() ) //make sure connection is really open
                {
                    dbConn.Close();
                    dbConn.Open();
                }
                return dbConn;
            }
            else
            {
                if ( !dbConnNoPool.Ping() ) //make sure connection is really open
                {
                    dbConnNoPool.Close();
                    dbConnNoPool.Open();
                }
                return dbConnNoPool;
            }
        }

        public void Close()
        {
            if ( ! pooled )
            {
                dbConnNoPool.Close();
            }                
        }

        public void Open()
        {
            var s = dbConnNoPool.State.ToString();
            if ( ! pooled )
            {
                if ( dbConnNoPool.State == System.Data.ConnectionState.Closed )
                {
                    dbConnNoPool.Open();
                }
                else if ( ! dbConnNoPool.Ping() )
                {
                    dbConnNoPool.Open();
                }                
            }
        }

        public bool SqlExec( string stmt, bool skipLogging = false )
        {
            MySqlConnection dbConn = GetConn();
            try
            {
                MySqlCommand myCommand = dbConn.CreateCommand();
                myCommand.CommandText = stmt;
                myCommand.ExecuteNonQuery();
                if ( log.IsDebugEnabled && skipLogging == false ) log.Debug( stmt );
                return true;
            }
            catch ( Exception e )
            {
                log.Error( e.Message + " " + stmt );
                return false;
            }
            finally
            {
                if ( pooled ) dbConn.Close();
            }
        }

        public bool SqlExec( string stmt, out long lastInsertId )
        {
            MySqlConnection dbConn = GetConn();
            lastInsertId = 0;
            try
            {
                MySqlCommand myCommand = dbConn.CreateCommand();
                myCommand.CommandText = stmt;
                myCommand.ExecuteNonQuery();
                if ( log.IsDebugEnabled ) log.Debug( stmt );
                lastInsertId = myCommand.InsertId;
                return true;
            }
            catch ( Exception e )
            {
                log.Error( e.Message + " " + stmt );
                return false;
            }
            finally
            {
                if ( pooled ) dbConn.Close();
            }
        }

        public string SqlFunc( string stmt )
        {
            MySqlConnection dbConn = GetConn();
            try
            {
                MySqlCommand myCommand = dbConn.CreateCommand();
                myCommand.CommandText = stmt;
                object retVal = myCommand.ExecuteScalar();
                if ( log.IsDebugEnabled ) log.Debug( stmt );
                if ( retVal != null && retVal.ToString() == "System.Byte[]" )
                    log.Warn( "cast error: " + stmt );
                return retVal == null ? "" : retVal.ToString();
            }
            catch(Exception e)
            {
                log.Error( e.Message + " " + stmt );
                return "";
            }
            finally
            {
                if ( pooled ) dbConn.Close();
            }
        }

        public bool SqlBatchExec( string stmts, Devart.Common.ScriptErrorEventHandler OnError )
        {
            MySqlConnection dbConn = GetConn();
            try
            {
                MySqlScript myScript = new MySqlScript(stmts, dbConn);
                if ( OnError != null )
                    myScript.Error += new Devart.Common.ScriptErrorEventHandler( OnError );
                myScript.Execute();
                if ( log.IsDebugEnabled ) log.Debug( stmts );
                return true;
            }
            catch ( Exception e )
            {
                log.Error( e.Message + " " + stmts );
                return false;
            }
            finally
            {
                if ( pooled ) dbConn.Close();
            }
        }

        /// caller has to close the reader (which auto closes the connection)
        public bool ExecReader( string stmt, ref MySqlDataReader dr, ref string statusMsg )
        {
            MySqlConnection dbConn = GetConn();
            try
            {
                MySqlCommand myCommand = dbConn.CreateCommand();
                myCommand.CommandText = stmt;
                myCommand.FetchAll = true;
                dr = myCommand.ExecuteReader( readerCmdBehavior );
                if ( log.IsDebugEnabled ) log.Debug( stmt );
                if ( dr.HasRows )
                {
                    return true;
                }
                else
                {
                    dr.Close();
                    dr = null;
                    return false;
                }
            }
            catch ( Exception e )
            {
                log.Error( e.Message + " " + stmt );
                statusMsg = e.Message;
                if ( dr != null ) dr.Close();
                dr = null;
                return false;
            }
            //note: caller will always close the connection 
        }

        public Dictionary<string,string> InitializeDefaults( string sql )
        {
            if ( String.IsNullOrEmpty( sql ) ) return null;

            MySqlConnection dbConn = GetConn();
            MySqlDataReader dr = null;
            try
            {
                var ini = new Dictionary<string, string>( StringComparer.InvariantCultureIgnoreCase );
                string err = "";
                if ( !ExecReader( sql, ref dr, ref err ) )
                {
                    return err == "" ? ini : null;
                }
                while ( dr.Read() )
                {
                    ini.Add( dr.GetString( 0 ).ToLower(), dr.GetString( 1 ) );
                }
                return ini;
            }
            catch ( Exception e )
            {
                log.Error( e.Message + " " + sql );
                return null;
            }
            finally
            {
                if ( dr != null ) dr.Close();
            }

        }

        public string GetDbSql( string sql, bool stripComments, bool stripNewlines = false )
        {            
            try
            {
                string s = SqlFunc( sql );
                if ( stripComments )
                    s = StripSqlComments( s, stripNewlines );
                return s;
            }
            catch ( Exception e )
            {
                log.Error( e.Message + " " + sql );
                return "";
            }
        }

        public string StripSqlComments( string sql, bool stripNewlines = false )
        {
            if ( string.IsNullOrEmpty( sql ) ) return "";

            StringBuilder sb = new StringBuilder();
            string [] ar = sql.Split( new char[] { '\n' } );
            for ( int i=0; i < ar.Length; i++ )
                if ( !( ar[ i ].Trim().StartsWith( "--" ) || ar[ i ].Trim().StartsWith( "#" ) ) )
                    sb.Append( ar[ i ] );
            if ( stripNewlines )
                return sb.ToString().Trim().Replace( '\n', ' ' ).Replace( '\r', ' ' );
            else
                return sb.ToString().Trim();
        }

        public string GetLastInsertId()
        {
            return SqlFunc( "select last_insert_id()" );
        }

    }

    public static class DbUtils
    {
        public static string Escape( this string value )  //note:  might be faster to inspect each char - jc
        {
            if ( String.IsNullOrEmpty( value ) ) return "";

            value = value.Replace( "\\", "\\\\" );  //backslash
            value = value.Replace( "'", "\\'" );    //single quote
            value = value.Replace( "ˈ", "\\ˈ" );    //single quote
            value = value.Replace( "′", "\\′" );    //single quote
            value = value.Replace( "’", "\\’" );    //single quote
            value = value.Replace( "\"", "\\\"" );  //double quote
            value = value.Replace( "\0", "\\0" );   //nul
            value = value.Replace( "\b", "\\b" );   //backspace
            value = value.Replace( "\t", "\\t" );   //tab
            value = value.Replace( "\n", "\\n" );   //LF
            value = value.Replace( "\r", "\\r" );   //CR
            value = value.Replace( "\x1A", "\\Z" ); //EOF on windows (sub);
            return value;
        }

        public static string StrToIntList( this string text, string defaultTo = "" )
        {
            if ( text.IsStrEmpty() ) return defaultTo;

            StringBuilder sb = new StringBuilder();
            string a;
            foreach ( string s in text.Split( new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries ) )
            {
                a = gp.JustInt( s, null );
                if ( a != null )
                    sb.Append( "," + a );
            }
            return sb.Length == 0 ? defaultTo : sb.ToString().Substring( 1 );
        }

        public static string StrToList( this string text, string defaultTo = "", string quote = "'" )
        {
            if ( text.IsStrEmpty() ) return defaultTo;

            StringBuilder sb = new StringBuilder();
            string a;
            foreach ( string s in text.Split( new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries ) )
            {
                a = s.Replace( "'", "'" );
                if ( a.HasValue() )
                    sb.AppendFormat( ",{0}{1}{0}", quote, a );
            }
            return sb.Length == 0 ? defaultTo : sb.ToString().Substring( 1 );
        }

        public static string EscStrQ( this string s )
        {
            return "'" + Escape( s ) + "'";
        }

        public static string Escape( this string s, int maxlen )
        {
            if ( s == null || s == "" ) return "";
            if ( s.Length > maxlen ) s = s.Substring( 0, maxlen );
            return Escape( s );
        }

        public static string EscStrQ( this string s, int maxlen )
        {
            if ( s == null || s == "" ) return "''";
            if ( s.Length > maxlen ) s = s.Substring( 0, maxlen );
            return "'" + Escape( s ) + "'";
        }

        public static string MyDate( this DateTime d, string format = "yyyyMMdd", string defaultTo = "null" )
        {
            return d == DateTime.MinValue ? defaultTo : d.ToString( format );
        }

        public static string MyTime( this DateTime d, string format = "HHmmss", string defaultTo = "null" )
        {
            return d == DateTime.MinValue ? defaultTo : d.ToString( format );
        }

        public static string MyDate( this DateTime? d, string format = "yyyyMMdd", string defaultTo = "null" )
        {
            if ( d == null ) return defaultTo;
            DateTime d2 = ( DateTime ) d;
            return d2 == null || d2 == DateTime.MinValue ? defaultTo : d2.ToString( format );
        }

        public static string MyTime( this DateTime? d, string format = "HHmmss", string defaultTo = "null" )
        {
            if ( d == null ) return defaultTo;
            DateTime d2 = ( DateTime ) d;
            return d2 == null || d2 == DateTime.MinValue ? defaultTo : d2.ToString( format );
        }

        public static string MyDateTime( this DateTime d, string format = "yyyyMMddHHmmss", string defaultTo = "null" )
        {
            return d == DateTime.MinValue ? defaultTo : d.ToString( format );
        }

        public static string MyInt( this int d, string emptyValue = "0", string defaultTo = "null" )
        {
            string a = d.ToString();
            return a == emptyValue ? defaultTo : d.ToString();
        }

        public static string MyDouble( this double d, string emptyValue = "0", string defaultTo = "null" )
        {
            string a = d.ToString();
            return a == emptyValue ? defaultTo : d.ToString();
        }

        public static string MyDecimal( this decimal d, string emptyValue = "0", string defaultTo = "null" )
        {
            string a = d.ToString();
            return a == emptyValue ? defaultTo : d.ToString();
        }

        public static string MyDecimal( this decimal? d, string emptyValue = "0", string defaultTo = "null" )
        {
            if ( d == null ) return defaultTo;
            string a = d.ToString();
            return a == emptyValue ? defaultTo : d.ToString();
        }

        public static string MyNullable<T>( this T? target, string format = "", string defaultTo = "null" )
            where T : struct, IFormattable
        {
            if ( target.HasValue )
            {
                if ( target is int? && format.IsStrEmpty() ) format = "0";
                else if ( target is DateTime? && format.IsStrEmpty() ) format = "yyyyMMddHHmmss";
                else if ( target is decimal? && format.IsStrEmpty() ) format = "0.0";

                return target.Value.ToString( format, CultureInfo.InvariantCulture );
            }
            return defaultTo;
        }
    }
}