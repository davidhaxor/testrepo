using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.IO;
using System.Net;
using System.Configuration;
using GenProcs.Utils;

namespace CmsResponse.Controllers
{
    public class ValuationController : BaseController
    {

        [HttpPost]
        public ActionResult StatusUpdate()
        {

            var m = new Models.Valuation.StatusUpdate();
            var aa = System.Configuration.ConfigurationManager.AppSettings;
            m.SetConfig( aa
                .Cast<string>()
                .ToDictionary( p => p, p => aa[ p ], StringComparer.InvariantCultureIgnoreCase )
                );

            using ( var reader = new StreamReader( ctxSvc.Request.InputStream ) )
            {
                m.Execute( reader.ReadToEnd() );
            }
            return Content( m.responseXml, "text/xml" );
        }

        private string GetConfig( string name )
        {
            return ConfigurationManager.AppSettings[ name ].ToStringDef( "" );
        }

    }
}
