using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text;

namespace MyServices
{
    public partial class Service1 : ServiceBase
    {
        private SendToCMS service;

        public Service1()
        {
            InitializeComponent();
        }

        protected override void OnStart(string[] args)
        {
            service = new SendToCMS();
            service.Start();
        }

        protected override void OnStop()
        {
            service.Stop();
        }
    }
}
