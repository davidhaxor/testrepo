using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Web;
using System.Web.Mvc;

namespace CmsResponse.Controllers
{
    public class BaseController : Controller
    {
        public IHttpContextService ctxSvc;

        protected override void Initialize( System.Web.Routing.RequestContext requestContext )
        {
            base.Initialize( requestContext );
            ctxSvc = MyServices.GetHttpContextService();
        }

        protected void AddValidationFailuresToModelState(IEnumerable<System.ComponentModel.DataAnnotations.ValidationResult> results)
        {
            if (results == null)
                return;

            foreach (var result in results)
            {
                foreach (var resultField in result.MemberNames)
                {
                    ModelState.AddModelError(resultField, result.ErrorMessage);
                }
            }
        }
    }

    public static class MyServices
    {
        public static IHttpContextService GetHttpContextService()
        {
            return new ContextServices();
        }
    }

    public interface IHttpContextService   //add a separation layer in case we want to inject another context (e.g. for TDD)
    {
        HttpContext Context { get; }
        HttpRequest Request { get; }
        HttpResponse Response { get; }
        NameValueCollection FormOrQuerystring { get; }
        String WebRootDir { get; }
        bool IsPost { get; }
        bool IsGet { get; }
    }

    public class ContextServices : IHttpContextService
    {
        public static IHttpContextService GetHttpContextService()
        {
            return new ContextServices();
        }
        public HttpContext Context { get { return HttpContext.Current; } }
        public HttpRequest Request { get { return HttpContext.Current.Request; } }
        public HttpResponse Response { get { return HttpContext.Current.Response; } }
        public NameValueCollection FormOrQuerystring
        {
            get
            {
                return Request.RequestType == "POST" ? Request.Form : Request.QueryString;
            }
        }
        public String WebRootDir 
        { 
            get { return HttpContext.Current.Server.MapPath( "~" ); } 
        }
        public bool IsPost
        {
            get { return Request.RequestType == "POST"; }
        }
        public bool IsGet
        {
            get { return Request.RequestType == "GET"; }
        }
    }

}