-- build script to deploy sql stmts
-- move parms into application.config
use resnet_if;

delete from resnet_if.parms where ptype='SendToCMSEnv';

insert into resnet_if.parms (ptype,pkey,res_id,descr) values
 ('SendToCMSEnv','listen_filter',0,'GetBpoPdf_*.txt')
,('SendToCMSEnv','listen_path',0,'C:\\temp\\CMS\\TestXmls\\WatchGenBpoPdf')
,('SendToCMSEnv','log_activity',0,'Yes')
,('SendToCMSEnv','refresh_config',0,'Refresh_Config.txt')
,('SendToCMSEnv','sleep_before_ms',0,'0')
,('SendToCMSEnv','timer_interval_mins',0,'5')
,('SendToCMSEnv','PauseMsBetweenTransactions',0,'0')

,('SendToCMSEnv','maxFilesize',0,'100000')
,('SendToCMSEnv','tempFileDir',0,'C:\\temp\\CMS\\TestXmls')
,('SendToCMSEnv','savePdfDir',0,'C:\\temp\\CMS\\TestXmls\\Pdf')
,('SendToCMSEnv','saveEnvDir',0,'C:\\temp\\CMS\\TestXmls\\Pdf')
,('SendToCMSEnv','bpoPicDir',0,'C:\\temp\\CMS')
-- ,('SendToCMSEnv','url',0,'https://uat.fncinc.com/envtool/scripts_low/bpo.asp')
,('SendToCMSEnv','url',0,'https://www.aiready.com/envtool/scripts_low/bpo.asp')
,('SendToCMSEnv','userId',0,'usres')
,('SendToCMSEnv','passwd',0,'26485wah')
,('SendToCMSEnv','requestTimeoutMs',0,'180000')
,('SendToCMSEnv','requestAccept',0,'text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8')
,('SendToCMSEnv','requestContentType',0,"text/xml; encoding='utf-8'")
,('SendToCMSEnv','requestMediaType',0,'')
;

/*

select `sql` from resnet_if.parms_sql where ptype='SendToCMSEnv' and pkey='Bpo2Env' and res_id=0
select `sql` from resnet_if.parms_sql where ptype='SendToCMSEnv' and pkey='Bpo2EnvPics' and res_id=0"

select concat('<add key="',pkey,'" value="',descr,'" />')
from resnet_if.parms where ptype='SendToCMSEnv';


*/