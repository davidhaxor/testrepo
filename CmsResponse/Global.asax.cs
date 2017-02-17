using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using GenProcs.MyDbl;

namespace CmsResponse
{
    public class MvcApplication : System.Web.HttpApplication
    {
        public static void RegisterRoutes( RouteCollection routes )
        {
            routes.IgnoreRoute( "{resource}.axd/{*pathInfo}" );

            routes.MapRoute(
                name: "Valuation",
                url: "Valuation/StatusUpdate",
                defaults: new { controller = "Valuation", action = "StatusUpdate" }
            );
            routes.MapRoute(
                name: "Default",
                url: "{controller}/{action}/{id}",
                defaults: new { controller = "Home", action = "Index", id = UrlParameter.Optional }
            );
        }

        protected void Application_Start()
        {
            AreaRegistration.RegisterAllAreas();
            RegisterRoutes( RouteTable.Routes );
            log4net.Config.XmlConfigurator.Configure();
        }
    }
}
