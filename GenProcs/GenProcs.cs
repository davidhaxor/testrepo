using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Globalization;
using System.Xml.Serialization;

namespace GenProcs.Utils
{
    public static class gp
    {
        public static bool IsValidAlphaNumeric( string text )
        {
            foreach ( char c in text )
            {
                if ( !char.IsLetterOrDigit( c ) )
                    return false;
            }
            return true;
        }

        public static bool IsValidSSN( string text )
        {
            return JustInt( text, "" ).Length == 9;
        }

        public static bool IsValidYear( string text, int from = 1900, int to = 2070 )
        {
            int i;
            if ( !int.TryParse( text, out i ) ) return false;
            return i >= from && i <= to;
        }

        public static bool IsValidZip( this string text )
        {
            StringBuilder sb = new StringBuilder();
            foreach ( char c in text )
            {
                if ( c == '-' )
                    continue; //ok
                else if ( char.IsDigit( c ) )
                    sb.Append( c );
                else
                    return false;
            }
            return sb.Length == 5 || sb.Length == 9;
        }

        public static bool IsValidPhone( this string text, bool requireAreaCode = false )
        {
            StringBuilder sb = new StringBuilder();
            foreach ( char c in text )
            {
                if ( " ()/-".IndexOf( c ) > -1 )
                    continue; //ok
                else if ( char.IsDigit( c ) )
                    sb.Append( c );
                else
                    return false;
            }
            return ( requireAreaCode && sb.Length >= 10 ) || ( !requireAreaCode && sb.Length >= 7 );
        }

        public static bool IsValidEmail( this string text )
        {
            return Regex.IsMatch( text,
              @"^(?("")("".+?""@)|(([0-9a-zA-Z]((\.(?!\.))|[-!#\$%&'\*\+/=\?\^`\{\}\|~\w])*)(?<=[0-9a-zA-Z])@))" +
              @"(?(\[)(\[(\d{1,3}\.){3}\d{1,3}\])|(([0-9a-zA-Z][-\w]*[0-9a-zA-Z]\.)+[a-zA-Z]{2,6}))$" );
        }

        public static bool StrToBool( this string text )
        {
            if ( String.IsNullOrEmpty( text ) ) return false;
            text = text.ToLower();
            return ",yes,true,y,on,".IndexOf( "," + text.Trim() + "," ) > -1 || text.IndexOf( "true" ) > -1 || text.IndexOf( "yes" ) > -1;
        }

        public static bool IsTrue( this string text )
        {
            if ( String.IsNullOrEmpty( text ) ) return false;
            text = text.ToLower();
            return ",yes,true,1,y,on,".IndexOf( "," + text.Trim() + "," ) > -1 || text.IndexOf( "true" ) > -1 || text.IndexOf( "yes" ) > -1;
        }

        public static bool IsFalse( this string text )
        {
            if ( String.IsNullOrEmpty( text ) ) return true;
            text = text.ToLower();
            return !( ",yes,true,1,y,on,".IndexOf( "," + text.Trim() + "," ) > -1 || text.IndexOf( "true" ) > -1 || text.IndexOf( "yes" ) > -1 );
        }

        public static string JustFloat( this string text, string defaultTo )
        {
            if ( String.IsNullOrEmpty( text ) ) return defaultTo;
            var s = new StringBuilder();
            foreach ( char c in text )
            {
                if ( Char.IsDigit( c ) || c == '.' || ( c == '-' && s.Length == 0 ) )
                    s.Append( c );
            }
            string a = s.ToString();
            if ( s.Length == 0 || a == "-" || a == "." )
                return defaultTo;
            else
                return a;
        }

        public static string LookupValue( this string text, string keysAndValues,  string defaultTo="", string delimBeg=",", string delimEnd="-" )
        {
            //e.g.  mo-Monday,tu-Tuesday,we-Wednesday

            if ( String.IsNullOrEmpty( text ) ) return defaultTo;

            string a = delimBeg + keysAndValues + delimBeg;
            string b = delimBeg + text + delimEnd;
            int i = ( a ).IndexOf( b, StringComparison.OrdinalIgnoreCase );
            if ( i > -1 )
            {
                string c = a.Substring( i + b.Length ).LeftOf( delimBeg );
                return c;
            }                
            return defaultTo;
        }

        public static string JustAlphaNumeric( this string text, string defaultTo = "" )
        {
            if ( String.IsNullOrEmpty( text ) ) return defaultTo;
            var s = new StringBuilder();
            foreach ( char c in text )
            {
                if ( Char.IsLetterOrDigit( c ) )
                    s.Append( c );
            }
            return s.ToString();
        }

        public static string JustAlpha( this string text, string defaultTo = "" )
        {
            if ( String.IsNullOrEmpty( text ) ) return defaultTo;
            var s = new StringBuilder();
            foreach ( char c in text )
            {
                if ( Char.IsLetter( c ) )
                    s.Append( c );
            }
            return s.ToString();
        }

        public static string JustInt( this string text, string defaultTo, bool allowSign = true )
        {
            if ( String.IsNullOrEmpty( text ) ) return defaultTo;
            var s = new StringBuilder();
            foreach ( char c in text )
            {
                if ( Char.IsDigit( c ) || ( c == '-' && s.Length == 0 && allowSign ) )
                    s.Append( c );
            }
            string a = s.ToString();
            if ( s.Length == 0 || a == "-" )
                return defaultTo;
            else
                return a;
        }

        public static bool Between( this int num, int lower, int upper, bool inclusive = true )
        {
            return inclusive ? num >= lower && num <= upper : num > lower && num < upper;
        }

        public static bool Between( this decimal num, decimal lower, decimal upper, bool inclusive = true )
        {
            return inclusive ? num >= lower  && num <= upper : num > lower && num < upper;
        }

        public static bool Between( this double num, double lower, double upper, bool inclusive = true )
        {
            return inclusive ? num >= lower && num <= upper : num > lower && num < upper;
        }

        public static bool Between( this DateTime date, DateTime lower, DateTime upper, bool includeTime = true, bool inclusive = true )
        {
            if ( includeTime )
                return inclusive ? date >= lower && date <= upper : date > lower && date < upper;
            else
                return inclusive ? date.Date >= lower.Date && date <= upper.Date : date > lower.Date && date.Date < upper.Date;
        }
        public static string GetDefParm( string text, string startDelim, string defaultTo, char endDelim = ',' )
        {
            string[] arr2 = text.Split( new string[] { startDelim }, 2, StringSplitOptions.None );
            if ( arr2.Length == 2 )
                return ( arr2[ 1 ] + "," ).Split( new char[] { endDelim }, 2 )[ 0 ];
            else
                return defaultTo;
        }

        public static int GetDefParm( string text, string token, int defaultTo )
        {
            int i;
            string[] arr2 = text.Split( new string[] { token }, 2, StringSplitOptions.None );
            if ( arr2.Length == 2 && int.TryParse( ( arr2[ 1 ] + "," ).Split( new char[] { ',' }, 2 )[ 0 ], out i ) )
                return i;
            else
                return defaultTo;
        }

        public static T GetEnumInStr<T>( this string text, T defaultTo )
        {
            if ( String.IsNullOrEmpty( text ) )
            {
                return defaultTo;
            }                
            foreach ( T ft in Enum.GetValues( typeof( T ) ) )
                if ( text.IndexOf( ft.ToString(), StringComparison.OrdinalIgnoreCase ) > -1 )
                {
                    return ft;
                }
            return defaultTo;
        }

        public static T StringToEnum<T>( this string text, T defaultTo )
        {
            if ( String.IsNullOrEmpty( text ) )
            {
                return defaultTo;
            }
            foreach ( T ft in Enum.GetValues( typeof( T ) ) )
                if ( text.Equals( ft.ToString(), StringComparison.OrdinalIgnoreCase ) )
                {
                    return ft;
                }
            return defaultTo;
        }

        public static Dictionary<string,T> EnumToDictonary<T>( this T items, bool lowerCase = false )
        {
            var dict = new Dictionary<string, T>();
            foreach ( T ft in Enum.GetValues( typeof( T ) ) )
            {
                dict.Add( lowerCase ? ft.ToString().ToLower() : ft.ToString(), ft );
            }
            return dict;
        }

        public static T IntToEnum<T>( this int value, T defaultTo )
        {
            if ( Enum.IsDefined( typeof( T ), value ) )
            {
                return ( T ) Enum.ToObject( typeof( T ), value );
            }
            return defaultTo;
        }

        public static T IntToEnum<T>( this uint value, T defaultTo )
        {
            if ( Enum.IsDefined( typeof( T ), value ) )
            {
                return ( T ) Enum.ToObject( typeof( T ), value );
            }
            return defaultTo;
        }

        public static string FormatPhone(this string text, char format = '(' )  //expects all digits
        {
            if ( text.Length < 7 ) return text;

            //paren - (714) 999-1234 xxxx 
            //dot   - 714.999.1234 xxxx
            //dash  - 714-999-1234 xxxx   
            string a,b,c;
            if      ( format == '(' ) { a = "("; b = ") "; c = "-"; }  //parens
            else if ( format == '.' ) { a = "" ; b = "." ; c = "."; }  //dot
            else if ( format == '-' ) { a = ""; b = "-"; c = "-"; }  //dash
            else if ( format == 'm' ) { a = ""; b = "."; c = "-"; }  //dot.dash
            else
                { a = "("; b = ") "; c = "-"; }  //parens

            StringBuilder sb = new StringBuilder();
            for ( int i = 0; i < text.Length; i++ )  //7142881001
            {
                if ( i == 0 ) sb.Append( a );
                else if ( i == 3 ) sb.Append( b );
                else if ( i == 6 ) sb.Append( c );
                else if ( i == 10 ) sb.Append( " " );
                sb.Append( text[ i ] );
            }
            return sb.ToString();
        }

        public static string FormatSSN( string text )
        {
            StringBuilder sb = new StringBuilder();
            for ( int i = 0; i < text.Length; i++ )  //111-22-3333
            {
                if ( Char.IsDigit( text[ i ] ) ) continue;
                else if ( i == 3 ) sb.Append( "-" );
                else if ( i == 5 ) sb.Append( "-" );
                sb.Append( text[ i ] );
            }
            return sb.ToString();
        }

        public static string GetProcName( string text, string token )
        {
            string a = ( text + "," ).Split( new char[] { ',' } )[ 0 ].Trim();
            if ( text.IndexOf( token, StringComparison.OrdinalIgnoreCase ) > -1 )
                return a;
            else
                return "";
        }

        public static string MaxLen( this string input, int max )
        {
            return ( ( input != null ) && ( input.Length > max ) ) ? input.Substring( 0, max ) : input;
        }

        public static void AppendLog( string fileName, string msg )
        {
            if ( msg.Length > 0 )
            {
                if ( fileName == null || fileName == "" )
                {
                    return;
                }
                DateTime dt = DateTime.Now;
                msg = dt.ToString( "yyyy-MM-dd HH:mm:ss ", System.Globalization.CultureInfo.InvariantCulture ) + msg;
                using ( StreamWriter w = File.AppendText( fileName ) )
                {
                    w.WriteLine( msg );
                }
            }
        }

        public static DateTime FromUnixTimestamp( this double timestamp )
        {
            DateTime origin = new DateTime( 1970, 1, 1, 0, 0, 0, 0 );
            return origin.AddSeconds( timestamp );
        }

        public static double ToUnixTimestamp( this DateTime date )
        {
            DateTime origin = new DateTime( 1970, 1, 1, 0, 0, 0, 0 );
            TimeSpan diff = date - origin;
            return Math.Floor( diff.TotalSeconds );
        }

        public static double ToDoubleDef( object value, double defaultTo )
        {
            try
            { return System.Double.Parse( value.ToString(), System.Globalization.NumberStyles.Any ); }
            catch ( FormatException )
            { return defaultTo; }
        }

        public static double ToDoubleDef( this string value, double defaultTo )
        {
            if ( String.IsNullOrEmpty( value ) ) return defaultTo;
            double i;
            return Double.TryParse( value, out i ) ? i : defaultTo;
        }

        public static decimal ToDecimalDef( this string value, decimal defaultTo )
        {
            if ( String.IsNullOrEmpty( value ) ) return defaultTo;
            Decimal i;
            return Decimal.TryParse( value, out i ) ? i : defaultTo;
        }

        public static bool InList( this string listOfValues, string item, string delim = "," )
        {
            return (delim+ listOfValues + delim).IndexOf( delim + item + delim, StringComparison.OrdinalIgnoreCase ) > -1;
        }

        public static bool In<T>( this T val, params T[] values ) where T : struct
        {
            return values.Contains( val );
        }

        public static string JsonEscape( this string value )
        {
            if ( String.IsNullOrEmpty( value ) ) return "";

            value = value.Replace( "\\", "\\\\" );  // backslash  must be first
            value = value.Replace( "\"", "\\\"" );  // double quote
            value = value.Replace( "'", "\'" );     // single quote
            value = value.Replace( "\b", "\\b" );   // backspace
            value = value.Replace( "\t", "\\t" );   // tab
            value = value.Replace( "\v", "\\v" );   // Vert tab
            value = value.Replace( "\f", "\\f" );   // FF
            value = value.Replace( "\n", "\\n" );   // LF
            value = value.Replace( "\r", "\\r" );   // CR
            return value;
        }

        public static int ToIntDef( this string value, int Default )
        {
            if ( String.IsNullOrEmpty( value ) ) return Default;
            int i;
            return Int32.TryParse( value, out i ) ? i : Default;
        }

        public static int? ToIntDefNull( this string value )
        {
            if ( String.IsNullOrEmpty( value ) ) return null;
            int i;
            if ( Int32.TryParse( value, out i ) )
                return i;
            return null;
        }

        public static byte ToByte(this int value )
        {
            return Convert.ToByte( value );
        }

        public static string ToProper( this string value )
        {
            if ( value.IsStrEmpty() ) return value;
            if ( value.Length == 1 ) return value.ToUpper();
            return value.Left(1).ToUpper() + value.Substring(1);
        }

        public static string ToStringDef( this string value, string Default )
        {
            return String.IsNullOrEmpty( value ) ? Default : value;
        }

        public static string ToStringDef( this DateTime value, string Default = "", string Format = "" )
        {
            if ( value == DateTime.MinValue ) return Default;
            return String.IsNullOrEmpty( Format ) ? value.ToString() : value.ToString( Format );
        }

        public static DateTime ToDateDef( this string value, DateTime defaultTo )
        {
            if ( String.IsNullOrEmpty( value ) ) return defaultTo;
            DateTime d;
            return DateTime.TryParse( value, out d ) ? d : defaultTo;
        }

        public static DateTime StrToDateDef( this string value, string dateFormat = "yyyy-MM-dd", DateTime? defaultTo = null)
        {
            DateTime def = defaultTo != null ? defaultTo.Value : DateTime.MinValue;
            if ( value.IsStrEmpty() ) return def;
            DateTime d;
            return DateTime.TryParseExact( value, dateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out d ) ? d : def;
        }

        public static string ReplaceStr( this string value, string findValue, string replValue ) //case insensitive
        {
            if ( String.IsNullOrEmpty(value) || String.IsNullOrEmpty(findValue) || replValue == null)
            {
                return value;
            }
            int i,lastPos = 0; 
            while ( true )
            {
                if ( lastPos >= value.Length )
                {
                    break;
                }
                i = value.IndexOf( findValue, lastPos, StringComparison.OrdinalIgnoreCase );
                if ( i == -1 )
                {
                    break;
                }
                value = value.Substring( 0, i ) + replValue + value.Substring( i + findValue.Length );
                lastPos = i + replValue.Length;
            }
            return value;
        }

        public static bool ReplaceStr( ref string value, string findValue, string replValue )
        {
            if ( String.IsNullOrEmpty(value) || String.IsNullOrEmpty(findValue) || replValue == null)
            {
                return false;
            }
            int i,lastPos = 0; bool found = false;
            while ( true )
            {
                if ( lastPos >= value.Length )
                {
                    break;
                }
                i = value.IndexOf( findValue, lastPos, StringComparison.OrdinalIgnoreCase );
                if ( i == -1 )
                {
                    break;
                }
                found = true;
                value = value.Substring( 0, i ) + replValue + value.Substring( i + findValue.Length );
                lastPos = i + replValue.Length;
            }
            return found;
        }

        // DES is the old "data encryption standard. key size 56 effective bits. uses 64-bit blocks

        public static string EncryptDES( string stringToEncrypt, string secretKey )
        {
            byte[] key = new byte[ 16 ];
            byte[] IV = new byte[ 16 ];

            try
            {
                secretKey = secretKey.PadRight( 8 );
                key = System.Text.Encoding.UTF8.GetBytes( secretKey.Substring( 0, 8 ) );
                IV = System.Text.Encoding.UTF8.GetBytes( secretKey.Substring( 0, 8 ) );
                byte[] inputByteArray = Encoding.UTF8.GetBytes( stringToEncrypt );
                
                DESCryptoServiceProvider des = new DESCryptoServiceProvider();
                MemoryStream ms = new MemoryStream();
                CryptoStream cs = new CryptoStream( ms, des.CreateEncryptor( key, IV ), CryptoStreamMode.Write );
                cs.Write( inputByteArray, 0, inputByteArray.Length );
                cs.FlushFinalBlock();
                return Convert.ToBase64String( ms.ToArray() );
            }
            catch ( Exception e )
            {
                return e.Message;
            }
        }

        public static string DecryptDES( string stringToDecrypt, string secretKey )
        {
            byte[] key = new byte[ 16 ];
            byte[] IV = new byte[ 16 ];
            byte[] inputByteArray = new byte[ stringToDecrypt.Length ];

            try
            {
                secretKey = secretKey.PadRight( 8 );
                key = System.Text.Encoding.UTF8.GetBytes( secretKey.Substring( 0, 8 ) );
                IV = System.Text.Encoding.UTF8.GetBytes( secretKey.Substring( 0, 8 ) );
                inputByteArray = Convert.FromBase64String( stringToDecrypt );
                
                DESCryptoServiceProvider des = new DESCryptoServiceProvider();
                MemoryStream ms = new MemoryStream();
                CryptoStream cs = new CryptoStream( ms, des.CreateDecryptor( key, IV ), CryptoStreamMode.Write );
                cs.Write( inputByteArray, 0, inputByteArray.Length );
                cs.FlushFinalBlock();
                Encoding encoding = System.Text.Encoding.UTF8;
                return encoding.GetString( ms.ToArray() );
            }
            catch ( Exception e )
            {
                return e.Message;
            }
        }

        // Rijndael supports block sizes of 128, 192, 256 bits. AES supports only 128 bit block sizes.

        public static byte[] EncryptRijndael( string plainText, byte[] Key, byte[] IV )
        {
            if ( String.IsNullOrEmpty(plainText) ) throw new ArgumentNullException( "plainText" );
            if ( Key == null || Key.Length <= 0 ) throw new ArgumentNullException( "Key" );
            if ( IV == null || IV.Length <= 0 ) throw new ArgumentNullException( "Key" );

            MemoryStream msEncrypt = null;
            CryptoStream csEncrypt = null;
            StreamWriter swEncrypt = null;
            RijndaelManaged aesAlg = null;
            try
            {
                aesAlg = new RijndaelManaged();
                aesAlg.Key = Key;
                aesAlg.IV = IV;

                ICryptoTransform encryptor = aesAlg.CreateEncryptor( aesAlg.Key, aesAlg.IV );

                // Create the streams used for encryption.
                msEncrypt = new MemoryStream();
                csEncrypt = new CryptoStream( msEncrypt, encryptor, CryptoStreamMode.Write );
                swEncrypt = new StreamWriter( csEncrypt );

                //Write all data to the stream.
                swEncrypt.Write( plainText );

            }
            finally
            {
                if ( swEncrypt != null ) swEncrypt.Close();
                if ( csEncrypt != null ) csEncrypt.Close();
                if ( msEncrypt != null ) msEncrypt.Close();
                if ( aesAlg != null ) aesAlg.Clear();
            }

            return msEncrypt.ToArray();
        }

        public static string DecryptRijndael( byte[] cipherText, byte[] Key, byte[] IV )
        {
            // Check arguments.
            if ( cipherText == null || cipherText.Length <= 0 ) throw new ArgumentNullException( "cipherText" );
            if ( Key == null || Key.Length <= 0 ) throw new ArgumentNullException( "Key" );
            if ( IV == null || IV.Length <= 0 ) throw new ArgumentNullException( "Key" );

            MemoryStream msDecrypt = null;
            CryptoStream csDecrypt = null;
            StreamReader srDecrypt = null;
            RijndaelManaged aesAlg = null;
            string plaintext = null;
            try
            {
                aesAlg = new RijndaelManaged();
                aesAlg.Key = Key;
                aesAlg.IV = IV;

                ICryptoTransform decryptor = aesAlg.CreateDecryptor( aesAlg.Key, aesAlg.IV );

                msDecrypt = new MemoryStream( cipherText );
                csDecrypt = new CryptoStream( msDecrypt, decryptor, CryptoStreamMode.Read );
                srDecrypt = new StreamReader( csDecrypt );

                plaintext = srDecrypt.ReadToEnd();
            }
            finally
            {
                if ( srDecrypt != null ) srDecrypt.Close();
                if ( csDecrypt != null ) csDecrypt.Close();
                if ( msDecrypt != null ) msDecrypt.Close();

                if ( aesAlg != null ) aesAlg.Clear();
            }
            return plaintext;
        }

        // AES (successor of DES) is symmetric encryption algorithm for US federal organizations 
        // AES accepts keys of 128, 192 or 256 bits (128 bits is already very unbreakable), this uses 128-bit blocks

        public static string EncryptAES( this string stringToEncrypt, string secretKey )
        {
            string encryptedText = "";

            if ( !string.IsNullOrEmpty( stringToEncrypt ) )
            {
                try
                {
                    using ( var aes = new AesManaged() )
                    {
                        
                        var plainTextBytes = System.Text.Encoding.Unicode.GetBytes( stringToEncrypt );
                        var saltBytes = Encoding.ASCII.GetBytes( secretKey.Length.ToString() );
                        byte[] cipherBytes = null;

                        var secretKeyDerived = new PasswordDeriveBytes( secretKey, saltBytes );
                        var transformer = aes.CreateEncryptor( secretKeyDerived.GetBytes( 32 ), secretKeyDerived.GetBytes( 16 ) );

                        using ( var memoryStream = new MemoryStream() )
                        {
                            using ( var cryptoStream = new CryptoStream( memoryStream, transformer, CryptoStreamMode.Write ) )
                            {
                                cryptoStream.Write( plainTextBytes, 0, plainTextBytes.Length );
                                cryptoStream.FlushFinalBlock();

                                cipherBytes = memoryStream.ToArray();

                                memoryStream.Close();
                                cryptoStream.Close();
                            }
                        }

                        encryptedText = Convert.ToBase64String( cipherBytes );
                    }
                }
                catch ( Exception )
                {
                    return encryptedText; //need to log, but genprocs does not have logger yet jc
                }
            }

            return encryptedText;
        }

        public static string DecryptAES( this string stringToDecrypt, string secretKey )
        {
            string decryptedText = "";

            if ( ! string.IsNullOrEmpty( stringToDecrypt ) )
            {
                try
                {
                    using ( AesManaged aes = new AesManaged() )
                    {
                        var encryptedBytes = Convert.FromBase64String( stringToDecrypt );
                        var saltBytes = Encoding.ASCII.GetBytes( secretKey.Length.ToString() );
                        byte[] plainTextBytes = null;
                        int decryptedCount = -1;

                        var secretKeyDerived = new PasswordDeriveBytes( secretKey, saltBytes );
                        var transformer = aes.CreateDecryptor( secretKeyDerived.GetBytes( 32 ), secretKeyDerived.GetBytes( 16 ) );

                        using ( var memoryStream = new MemoryStream( encryptedBytes ) )
                        {
                            using ( var cryptoStream = new CryptoStream( memoryStream, transformer, CryptoStreamMode.Read ) )
                            {
                                plainTextBytes = new byte[ encryptedBytes.Length ];
                                decryptedCount = cryptoStream.Read( plainTextBytes, 0, plainTextBytes.Length );

                                memoryStream.Close();
                                cryptoStream.Close();
                            }
                        }

                        decryptedText = Encoding.Unicode.GetString( plainTextBytes, 0, decryptedCount );
                    }
                }
                catch ( Exception)
                {
                    return decryptedText;  //need to log, but genprocs does not have logger yet jc
                }
            }

            return decryptedText;
        }

        public static bool ValidTime( this string value )
        {
            value = value.JustInt( "", false );
            if ( String.IsNullOrEmpty( value ) || value.Length > 4 )  //hhmm - 12 hour
            {
                return false;
            }
            value = value.PadLeft( 4, '0' );
            int h = value.Substring( 0, 2 ).ToIntDef( 0 );
            int m = value.Substring( 2, 2 ).ToIntDef( 0 );
            return ( h <= 12 && m <= 59 );
        }

        public static string LeftOf( this string value, string findStr )
        {
            if ( String.IsNullOrEmpty( value ) || String.IsNullOrEmpty( findStr ) )
            {
                return value;
            }
            int i = value.IndexOf( findStr, StringComparison.OrdinalIgnoreCase );

            if ( i == -1 ) return value;
            return value.Substring( 0, i );
        }

        public static string RightOf( this string value, string findStr )
        {
            if ( String.IsNullOrEmpty( value ) || String.IsNullOrEmpty( findStr ) )
            {
                return value;
            }
            int i = value.IndexOf( findStr, StringComparison.OrdinalIgnoreCase );

            if ( i == -1 ) return value;
            int len = findStr.Length;
            if ( i + len >= value.Length ) return "";

            return value.Substring( i + len );
        }

        public static string RightOfR( this string value, string findStr )
        {
            if ( String.IsNullOrEmpty( value ) || String.IsNullOrEmpty( findStr ) )
            {
                return value;
            }
            int i = value.LastIndexOf( findStr, StringComparison.OrdinalIgnoreCase );

            if ( i == -1 ) return value;
            int len = findStr.Length;
            if ( i + len >= value.Length ) return "";
            return value.Substring( i + len );
        }

        public static string Left( this string value, int length )
        {
            if ( String.IsNullOrEmpty( value ) || length < 0 )
            {
                return value;
            }
            return value.Length <= length ? value : value.Substring( 0, length );
        }

        public static string Right( this string value, int length )
        {
            if ( String.IsNullOrEmpty( value ) || length < 0 )
            {
                return value;
            }
            return value.Length <= length ? value : value.Substring( value.Length - length, length );
        }

        public static string RemoveDiacritics( this string text )
        {
            string formDText = text.Normalize( NormalizationForm.FormD );
            var sb = new StringBuilder();

            for ( int i = 0; i < formDText.Length; i++ )
            {
                UnicodeCategory uc = CharUnicodeInfo.GetUnicodeCategory( formDText[ i ] );
                if ( uc != UnicodeCategory.NonSpacingMark )
                {
                    sb.Append( formDText[ i ] );
                }
            }
            return ( sb.ToString().Normalize( NormalizationForm.FormC ) );
        }

        public static string MD5Hash( this string text )
        {
            System.Security.Cryptography.MD5 md5 = new System.Security.Cryptography.MD5CryptoServiceProvider();
            return System.Text.RegularExpressions.Regex.Replace(
                BitConverter.ToString(
                    md5.ComputeHash( ASCIIEncoding.Default.GetBytes( text ) )
                    )
                , "-", ""
            );
        }

        // http: //softwaredevelopment.gr/62/hashing-password-with-sha-256-in-c/

        public static string SHA256Hash(this string text )
        {
            byte[] inputBytes  = Encoding.UTF8.GetBytes( text );
            byte[] hashedBytes = ( new SHA256CryptoServiceProvider() ).ComputeHash( inputBytes );
            return BitConverter.ToString( hashedBytes ).Replace( "-", string.Empty ).ToLower();
        }

        // http: //weblogs.asp.net/pabloperalta/archive/2008/09/19/how-to-generate-a-hash-with-a-secret-key.aspx

        public static string GetMACHash( this string text, string secretKey )
        {
            //  secret key shared by sender and receiver.
            byte[] secretKeyBytes = new Byte[ 64 ];
            secretKeyBytes = System.Text.UTF8Encoding.UTF8.GetBytes( secretKey );
            HMACSHA256 myhmacsha256 = new HMACSHA256( secretKeyBytes );
            byte[] bytedText = System.Text.UTF8Encoding.UTF8.GetBytes( text );
            byte[] hashValue = myhmacsha256.ComputeHash( bytedText );
            return Convert.ToBase64String( hashValue ).TrimEnd( "=".ToCharArray() );
        }

        public static IEnumerable<T[]> Combinations<T>( this IList<T> argList, int argSetSize )
        {
            /// http://dotnetgeek.tumblr.com/post/5205119053/c-combination-subset-extension-method

            if ( argList == null ) throw new ArgumentNullException( "argList" );
            if ( argSetSize <= 0 ) throw new ArgumentException( "argSetSize Must be greater than 0", "argSetSize" );
            return combinationsImpl( argList, 0, argSetSize - 1 );
        }

        private static IEnumerable<T[]> combinationsImpl<T>( IList<T> argList, int argStart, int argIteration, List<int> argIndicies = null )
        {
            argIndicies = argIndicies ?? new List<int>();
            for ( int i = argStart; i < argList.Count; i++ )
            {
                argIndicies.Add( i );
                if ( argIteration > 0 )
                {
                    foreach ( var array in combinationsImpl( argList, i + 1, argIteration - 1, argIndicies ) )
                    {
                        yield return array;
                    }
                }
                else
                {
                    var array = new T[ argIndicies.Count ];
                    for ( int j = 0; j < argIndicies.Count; j++ )
                    {
                        array[ j ] = argList[ argIndicies[ j ] ];
                    }

                    yield return array;
                }
                argIndicies.RemoveAt( argIndicies.Count - 1 );
            }
        }

        public static bool IsStrEmpty( this string text )
        {
            return ( text == null || text.Length == 0 || text.Trim().Length == 0 );
        }

        public static bool HasValue( this string text )
        {
            return ( text != null && text.Trim().Length > 0 );
        }

        public static bool HasItems<T>( this IEnumerable<T> items )
        {
            return ( items != null && items.Any() ); // just does a .movenext
        }

        public static bool IsEmpty<T>( this IEnumerable<T> items )
        {
            return ( items == null || ! items.Any() ); // just does a .movenext
        }

        public static string FormatWith( this string formatStr, params object[] objects )
        {
            if ( objects.Length == 0 ) return formatStr;
            try
            {
                return string.Format( formatStr, objects );
            }
            catch ( Exception e )
            {
                return e.Message;
            }
        }

        public static string Utf8ToLatin1( this string text )
        {
            Encoding iso = Encoding.GetEncoding("ISO-8859-1");
            Encoding utf8 = Encoding.UTF8;
            byte[] utfBytes = utf8.GetBytes( text );
            byte[] isoBytes = Encoding.Convert( utf8, iso, utfBytes );
            return iso.GetString( isoBytes );
        }

        public static byte[] ToUTF8( this string text )
        {
            return Encoding.UTF8.GetBytes( text );
        }

        private static Regex _htmlRegex = new Regex( @"<(.|\n)*?>", RegexOptions.Compiled );
        //private static Regex _htmlRegex = new Regex( "<.*?>", RegexOptions.Compiled );

        public static string StripHTML( this string text )
        {
            return _htmlRegex.Replace( text, string.Empty );
        }

        public static string SerializeObject<T>( this T toSerialize )
        {
            var xmlSerializer = new XmlSerializer( toSerialize.GetType() );

            using ( var textWriter = new StringWriterUtf8() )
            {
                xmlSerializer.Serialize( textWriter, toSerialize );
                return textWriter.ToString();
            }
        }

        public static T Deserialize<T>( this string text ) where T : class
        {
            System.Xml.Serialization.XmlSerializer ser = new System.Xml.Serialization.XmlSerializer( typeof( T ) );

            using ( StringReader sr = new StringReader( text ) )
            {
                return ( T ) ser.Deserialize( sr );
            }                
        }

        public class StringWriterUtf8 : System.IO.StringWriter
        {
            public override Encoding Encoding
            {
                get
                {
                    return Encoding.UTF8;
                }
            }
        }


        public static TValue GetValueDef<TKey,TValue>(this Dictionary<TKey,TValue> dict, TKey key, TValue defaultValue = default( TValue ) )
        {
            if ( dict == null || ! dict.ContainsKey( key ) ) return defaultValue;
            return dict[ key ];
        }

    } //class
}