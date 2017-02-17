using System;
using System.Collections.Specialized;
using System.Configuration;
using System.IO;
using System.Net;
using System.Text;
using System.Web;

namespace GenProcs.Utils
{
    public static class wp
    {
        public const string FileLengthZero = "file length = 0";

        static wp()
        {
        }

        public static string GetUrlPart( string urlPath, string segment )
        {
            if ( urlPath == null || urlPath == "" || segment == null || segment == "" ) return "";

            string cmd = "";
            string[] parts;
            if ( urlPath.IndexOf( '?' ) > -1 )
            {
                parts = urlPath.Split( new char[] { '?' }, 2 )[ 0 ].Split( new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries );
                cmd = urlPath.Split( new char[] { '?' }, 2 )[ 1 ].Trim();
            }
            else
            {
                parts = urlPath.Split( new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries );
            }
            segment = segment.ToLower();
            if ( segment == "cmd" )
                return cmd;
            //Get url_# segment (i.e. 1,2,3,4)
            if ( segment.StartsWith( "url_", StringComparison.OrdinalIgnoreCase ) )
            {
                for ( int i = 1; i < parts.Length + 1; i++ )
                {
                    if ( segment == "url_" + i.ToString() )
                        return parts[ i - 1 ];
                }
                return "";
            }
            if ( segment.StartsWith( "url.", StringComparison.OrdinalIgnoreCase ) )
            {
                segment = segment.Substring( 4 );
                for ( int i = 0; i < parts.Length; i++ )
                {
                    if ( segment.Equals( parts[ i ], StringComparison.OrdinalIgnoreCase ) )
                        return parts.Length > i + 1 ? parts[ i + 1 ] : "";
                }
                return "";
            }
            return "";
        }

        public static bool StaticViewExists( ref string path, string baseDir )
        {
            string a = path.ToLower().Replace( "/static/", "" ).Replace( "static/", "" );

            if ( !a.EndsWith( ".aspx", StringComparison.OrdinalIgnoreCase ) )
            {
                a += ".aspx";
            }
            if ( File.Exists( baseDir + @"\Views\Static\" + a ) )
            {
                path = "~/Views/Static/" + a;
                return true;
            }
            return false;

        }

        public static StringBuilder GetIntCbs( NameValueCollection nvc, string prefix )
        {
            var sb = new StringBuilder();
            foreach ( string k in nvc )
            {
                if ( k.StartsWith( prefix ) )
                {
                    string v = gp.JustInt( k, "", false );
                    if ( v.Length > 0 )
                        sb.Append( "," + v );
                }
            }
            return sb;
        }

        public static StringBuilder GetCbNames( NameValueCollection nvc, string prefix )
        {
            var sb = new StringBuilder();
            foreach ( string k in nvc )
            {
                if ( k != null && k.StartsWith( prefix ) )
                {
                    string v = k.Substring( prefix.Length );
                    if ( v.Length > 0 )
                        sb.Append( "," + v );
                }
            }
            if ( sb.Length > 0 ) sb[ 0 ] = ' ';
            return sb;
        }

        public static string UrlEncode( this string value )
        {
            return HttpUtility.UrlEncode( value );
        }
        
        public static string UrlDecode( this string value )
        {
            return HttpUtility.UrlDecode( value );
        }

        public static string HtmlEncode( this string value )
        {
            return HttpUtility.HtmlEncode( value );
        }

        public static string HtmlDecode( this string value )
        {
            return HttpUtility.HtmlDecode( value );
        }

        public static string JavaScriptEscape( this string value )
        {
            if ( String.IsNullOrEmpty( value ) )
                return "";
            return value
                .Replace( "\\", "\\\\" )  //backslash
                .Replace( "\b", "\\b" )   //backspace
                .Replace( "\f", "\\f" )   //FF
                .Replace( "\n", "\\n" )   //LF
                .Replace( "\r", "\\r" )   //CR
                .Replace( "\t", "\\t" )   //tab
                .Replace( "'", "\\'" ) //single quote
                .Replace( "\"", "\\\"" )  //double quote
                ;
        }
        
        public static bool SaveFileFromUrl( string responseFileName, out string errorMsg, 
            Func<HttpWebRequest> CreateRequest, int maxFileSize = 500000 )
        {
            errorMsg = "";
            byte[] content;
            HttpWebRequest request = CreateRequest();

            if ( request == null ) return false;

            try
            {
                WebResponse response = request.GetResponse();
                Stream stream = response.GetResponseStream();
                using ( BinaryReader br = new BinaryReader( stream ) )
                {
                    content = br.ReadBytes( maxFileSize );
                    br.Close();
                }
                response.Close();
                if ( content.Length == 0 )
                {
                    errorMsg = FileLengthZero;
                    return false;
                }
            }
            catch ( Exception e )
            {
                errorMsg = e.Message;
                return false;
            }

            FileStream fs = new FileStream( responseFileName, FileMode.Create );
            BinaryWriter bw = new BinaryWriter( fs );
            try
            {
                bw.Write( content );
            }
            catch ( WebException e )
            {
                errorMsg = e.Message;
                return false;
            }
            finally
            {
                fs.Close();
                bw.Close();
            }
            if ( File.Exists( responseFileName ) )
            {
                long j = new FileInfo( responseFileName ).Length;
                if ( j == 0 )
                {
                    errorMsg = ( errorMsg + " " + FileLengthZero ).Trim();
                    return false;
                }
            }

            return true;
        }

        public static bool SaveToFileNameFromUrl( string responseFileName, out string errorMsg,
            Func<HttpWebRequest> CreateRequest, int maxFileSize = 500000 )
        {
            errorMsg = "";
            HttpWebRequest request = CreateRequest();

            if ( request == null ) return false;

            try
            {
                WebResponse response = request.GetResponse();
                using ( var stream = File.Create( responseFileName ) )
                    response.GetResponseStream().CopyTo( stream );
            }
            catch ( Exception e )
            {
                errorMsg = e.Message;
                return false;
            }

            if ( File.Exists( responseFileName ) )
            {
                long j = new FileInfo( responseFileName ).Length;
                if ( j == 0 )
                {
                    errorMsg = ( errorMsg + " " + FileLengthZero ).Trim();
                    return false;
                }
            }

            return true;
        }

        public static bool SaveFileFromUrl( string fileName, string url, out string msg,
            int timeOut = 120000, bool allowRedirect = false, int maxFileSize = 500000 )
        {
            msg = "";
            byte[] content;
            HttpWebRequest request = ( HttpWebRequest ) WebRequest.Create( url );
            request.AllowAutoRedirect = allowRedirect;
            request.Timeout = timeOut;

            try
            {
                WebResponse response = request.GetResponse();
                Stream stream = response.GetResponseStream();
                using ( BinaryReader br = new BinaryReader( stream ) )
                {
                    content = br.ReadBytes( maxFileSize );
                    br.Close();
                }
                response.Close();
                if ( content.Length == 0 )
                {
                    msg = FileLengthZero;
                    return false;
                }
            }
            catch ( Exception e )
            {
                msg = e.Message;
                return false;
            }

            FileStream fs = new FileStream( fileName, FileMode.Create );
            BinaryWriter bw = new BinaryWriter( fs );
            try
            {
                bw.Write( content );
            }
            catch ( WebException e )
            {
                msg = e.Message;
                return false;
            }
            finally
            {
                fs.Close();
                bw.Close();
            }
            if ( File.Exists( fileName ) )
            {
                long j = new FileInfo( fileName ).Length;
                if ( j == 0 )
                {
                    msg = ( msg + " " + FileLengthZero ).Trim();
                    return false;
                }
            }

            //return msg if it's redirected
            if ( allowRedirect && request != null && url.ToLower() != request.Address.ToString().ToLower() )
            {
                msg = request.Address.ToString();
            }
            return true;
        }

        public static string UrlGetRequest( string url, out string msg, int timeOutMs = 10000 )
        {
            msg = "";
            HttpWebRequest request = WebRequest.Create( url ) as HttpWebRequest;
            request.AllowAutoRedirect = false;
            request.Timeout = timeOutMs;
            try
            {
                using ( HttpWebResponse response = request.GetResponse() as HttpWebResponse )
                {
                    StreamReader reader = new StreamReader( response.GetResponseStream() );

                    return reader.ReadToEnd();
                }
            }
            catch ( WebException e )
            {
                msg = e.Message;
                return "";
            }
        }

        public static string DateTimeAgo( this DateTime dt, string suffix = " ago" )
        {
            if ( dt == DateTime.MinValue ) return "";

            TimeSpan ts = DateTime.Now.Subtract( dt );
            long secs = ( long ) ts.TotalSeconds;
            string ago = "";
            if      ( ts.TotalSeconds < 2  ) ago = "1 sec";
            else if ( ts.TotalSeconds < 60 ) ago = String.Format( "{0:0} secs", ts.TotalSeconds );
            else if ( ts.TotalMinutes < 2  ) ago = "1 min";
            else if ( ts.TotalMinutes < 60 ) ago = String.Format( "{0:0} mins", ts.TotalMinutes );
            else if ( ts.TotalHours   < 2  ) ago = "1 hr";
            else if ( ts.TotalHours   < 24 ) ago = String.Format( "{0:0} hrs", ts.TotalHours );
            else if ( ts.TotalDays    < 2  ) ago = "1 day";
            else if ( ts.TotalDays    > 365) ago = String.Format( "{0:0}+ yr{1}", Math.Round( ts.TotalDays / 365), ts.TotalDays < 730 ? "" : "s" );
            else if ( ts.TotalDays    > 62 ) ago = String.Format( "{0:0} mos", Math.Round( ts.TotalDays / 30 ) );
            else
                ago = String.Format( "{0:0} days", ts.TotalDays);

            return ago + suffix;
        }
        
        public static double Average( double n, double d, int places = 0 )
        {
            if ( n == 0 || d == 0 ) 
                return 0;
            double r = n / d;
            return Math.Round( r, places );
        }

        public static string XmlEncode( this string value )
        {
            return value.Replace( "&", "&amp;" )
                .Replace( "<", "&lt;" )
                .Replace( ">", "&gt;" )
                .Replace( "\"", "&quot;" )
                .Replace( "'", "&apos;" );
        }
    
        public static string FormatHttpUrl( this string text, bool SSL = false )
        {
            if ( SSL )
            {
                if ( text.StartsWith( "https://", StringComparison.Ordinal ) ) return text;
                if ( text.StartsWith( "//", StringComparison.Ordinal ) ) return "https:" + text;
                return "https://" + text;
            }
            else
            {
                if ( text.StartsWith( "http://", StringComparison.Ordinal ) ) return text;
                if ( text.StartsWith( "//", StringComparison.Ordinal ) ) return "http:" + text;
                return "http://" + text;
            }
        }

        public static string StripScheme( this string text )
        {
            if ( text.IsStrEmpty() ) return "";
            text = text.ReplaceStr( "http:", "" ).ReplaceStr( "https:", "" );
            return text.StartsWith( "//" ) ? text.RightOf("//") : text;
        }

        public static string WebServer( string prefix = "//", bool includeNonDefautPort = true )
        {
            var url = HttpContext.Current.Request.Url;
            if ( includeNonDefautPort && ! url.IsDefaultPort )
            {
                return prefix + url.Host + ":" + url.Port;

            }
            return prefix + url.Host;
        }
 
        public static string AppendContentTextFile( this string filePath, bool useMinFile = false, string minAltPath ="", bool comment = true)
        {
            string results = comment ? "/*{0}*/\n".FormatWith( filePath ) : "";

            filePath = HttpContext.Current.Server.MapPath( filePath );
            
            if ( useMinFile )
            {
                string ext = Path.GetExtension( filePath );
                string mf = minAltPath.HasValue() ? minAltPath : Path.ChangeExtension( filePath, ".min." + ext.Replace(".","") );
                if ( File.Exists( mf ) )
                {
                    return results + File.ReadAllText( mf ).Trim();
                }                
            }
            if ( File.Exists( filePath) )
            {
                return results + File.ReadAllText( filePath ).Trim();
            }
            return results;
        }

        public static string FormatIfValue( this string value, string format = "", string defaultTo = "" )
        {
            if ( value.IsStrEmpty() )
                return defaultTo;
            else if ( format.Contains( "{0" ) )
                return String.Format( format, value );
            else
                return value;
        }

    }

}