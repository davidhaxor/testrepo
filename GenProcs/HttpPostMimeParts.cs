using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.IO;
using System.Net;

namespace GenProcs.Utils
{
    // note: copied from Federicos fine work at: BPOGenerator\Generators\FNCGenerator.cs
    public class MimePart
    {
        public byte[] Header { get; protected set; }
        public byte[] Data { get; set; }

        public List<String> Headers { get; set; }

        public long EncodeHeaders( string boundary )
        {
            var sb = new StringBuilder( "--" + boundary );
            sb.AppendLine();
            foreach ( var item in Headers ) sb.AppendLine( item );
            sb.AppendLine();

            Header = sb.ToString().ToUTF8();

            return Header.Length + Data.Length + 2;
        }
    }

    public class HttpPostMimeParts
    {
        public List<MimePart> PartsList { get; set; }
        public string Boundary { get; set; }
        public string ContentType { get; set; }
        public byte[] ContentFooter { get; set; }
        public byte[] PartFooter { get; set; }

        public HttpPostMimeParts()
        {
            PartsList = new List<MimePart>();
            Boundary = "----------" + DateTime.Now.Ticks.ToString( "x" );
            ContentType = "multipart/form-data; boundary=" + Boundary;
            ContentFooter = ( "--" + Boundary + "--\r\n" ).ToUTF8();
            PartFooter = "\r\n".ToUTF8();
        }

        public void AddPart( string text, params string[] headers  )
        {
            PartsList.Add( new MimePart
            {
                Headers = new List<String>( headers ),
                Data = text.ToUTF8()
            });
        }

        public void AddPart( byte[] data, params string[] headers )
        {
            PartsList.Add( new MimePart
            {
                Headers = new List<String>( headers ),
                Data = new byte[ data.Length ]
            });
            Array.Copy( data, PartsList[ PartsList.Count - 1 ].Data, data.Length );
        }

        public long ContentLength
        {
            get
            {
                return ContentFooter.Length + PartsList.Sum( s => s.EncodeHeaders( Boundary ) );
            }
        }

        public void SetStream( HttpWebRequest request )
        {
            byte[] buffer = new byte[ 8192 ];
            int read;

            using ( Stream s = request.GetRequestStream() )
            {
                foreach ( var part in PartsList )
                {
                    s.Write( part.Header, 0, part.Header.Length );
                    using ( MemoryStream stream = new MemoryStream( part.Data ) )
                    {
                        while ( ( read = stream.Read( buffer, 0, buffer.Length ) ) > 0 )
                        {
                            s.Write( buffer, 0, read );
                        }
                    }
                    s.Write( PartFooter, 0, PartFooter.Length );
                }
                s.Write( ContentFooter, 0, ContentFooter.Length );
            }
        }

        public void LogRequestFile( string fileName )
        {
            var reqInfo = new StringBuilder();
            foreach ( var part in PartsList )
            {
                reqInfo.Append( Encoding.UTF8.GetString( part.Header ) );
                reqInfo.Append( Encoding.UTF8.GetString( part.Data ) );
                reqInfo.Append( Encoding.UTF8.GetString( PartFooter ) );
            }
            reqInfo.Append( Encoding.UTF8.GetString( ContentFooter ) );

            File.WriteAllText( fileName, reqInfo.ToString() );
    }
}

}
