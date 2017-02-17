using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Devart.Data.MySql;
using GenProcs.Utils;
using log4net;

namespace ResNet.Bpo.CMS
{
    public class GetBpoPdfEnv
    {
        public Dictionary<string, string> dictConfig { get; set; }
        public Config config { get; set; }
        public string errorMsg;
        public int picCount { get; set; }
        public string pid { get; internal set; }
        public string filePdfPath { get; set; }
        public string fileEnvPath { get; set; }
        public bool pdfOnly { get; set; }

        private static readonly log4net.ILog log = LogManager.GetLogger( typeof( GetBpoPdfEnv ) );
        private List<string> nof = new List<string>();
        private MySqlDataReader dataset;
        private Dictionary<string, int> flds;

        private string responseFile;
        private int runTid;
        private FORMINFO cmsBpo;
        private List<BpoPic> picList;

        #region internal Classes

        public class Config
        {
            public int maxFilesize { get; set; }
            public string tempFileDir { get; set; }
            public bool saveTempFiles { get; set; }
            public string url { get; set; }
            public int requestTimeoutMs { get; set; }
            public string requestAccept { get; set; }
            public string requestContentType { get; set; }
            public string requestMediaType { get; set; }
            public string savePdfDir { get; set; }
            public string saveEnvDir { get; set; }
            public string userId { get; set; }
            public string passwd { get; set; }
            public string bpoPicDir { get; set; }
        }

        public class SendResults
        {
            public bool isSubmitOk { get; set; }
            public string submitError { get; set; }
            public RESPONSE response { get; set; }
        }

        public class BpoPic
        {
            public string fileName { get; set; }
            public string fileNameFnc { get; set; }  //FNC won't except .jpeg so renaming to .jpg in xml+mime
            public string fileDir { get; set; }
            public string dateTaken { get; set; }
            public string fileType { get; set; }
            public string description1 { get; set; }
            public string description2 { get; set; }
            public string description3 { get; set; }
            public bool addedToXml { get; set; }
            public string slot { get; set; }
        }

        #endregion

        public GetBpoPdfEnv()
        {
        }

        public GetBpoPdfEnv( Dictionary<string, string> dictConfigIn )
        {
            config = new Config()
            {
                maxFilesize        = dictConfigIn.GetValueDef( "envMaxFilesize" ).ToIntDef( 100000 ),
                tempFileDir        = dictConfigIn.GetValueDef( "envTempFileDir" ),
                savePdfDir         = dictConfigIn.GetValueDef( "envSavePdfDir" ),
                saveEnvDir         = dictConfigIn.GetValueDef( "envSaveEnvDir" ),
                bpoPicDir          = dictConfigIn.GetValueDef( "envBpoPicDir" ),
                saveTempFiles      = dictConfigIn.GetValueDef( "envSaveTempFiles" ).IsTrue(),
                url                = dictConfigIn.GetValueDef( "envUrl" ),
                userId             = dictConfigIn.GetValueDef( "envUserId" ),
                passwd             = dictConfigIn.GetValueDef( "envPasswd" ),
                requestTimeoutMs   = dictConfigIn.GetValueDef( "envRequestTimeoutMs" ).ToIntDef( 120000 ),
                requestAccept      = dictConfigIn.GetValueDef( "envRequestAccept" ),
                requestContentType = dictConfigIn.GetValueDef( "envRequestContentType" ),
                requestMediaType   = dictConfigIn.GetValueDef( "envRequestMediaType" ),
            };
        }

        public static string CheckConfig( Dictionary<string, string> dictConfig )
        {
            var errs = new StringBuilder();

            string bpoPicDir = dictConfig.GetValueDef( "envBpoPicDir" );
            string saveEnvDir = dictConfig.GetValueDef( "envSaveEnvDir" );
            string savePdfDir = dictConfig.GetValueDef( "envSavePdfDir" );
            string tempFileDir = dictConfig.GetValueDef( "envTempFileDir" );

            if ( !Directory.Exists( bpoPicDir ) ) errs.Append( ", Invalid envBpoPicDir: " + bpoPicDir );
            if ( !Directory.Exists( saveEnvDir ) ) errs.Append( ", Invalid envSaveEnvDir: " + saveEnvDir );
            if ( !Directory.Exists( savePdfDir ) ) errs.Append( ", Invalid envSavePdfDir: " + savePdfDir );
            if ( !Directory.Exists( tempFileDir ) ) errs.Append( ", Invalid envTempFileDir: " + tempFileDir );

            if ( errs.Length > 0 )
            {
                errs.Insert( 0, "Required BPO GetEnvPdf Config Values missing: " + errs.ToString().Substring( 1 ) );
            }

            return errs.ToString();
        }

        public bool Execute( int runTid, MySqlDataReader dr, MySqlDataReader drPics )
        {
            errorMsg = "";
            picCount = 0;

            // 1. dbInfo to XML

            List<BpoPic> picList = GenBpoPicList( drPics );
            var sendXml = BpoData2Xml( runTid, dr, picList );

            if ( errorMsg.HasValue() ) return false;

            // 2. send request

            var results = PostBpoRequest( runTid, sendXml, picList );

            if ( errorMsg.HasValue() ) return false;

            if ( results == null || results.submitError == null ) return False( "No Response from server: " + runTid.ToString()  );
            if ( results.submitError.HasValue() ) return False( results.submitError + " " + runTid.ToString() );

            // 3. if successfull get/save the files....

            DownloadFiles( runTid, results ); //error reporting

            return errorMsg.IsStrEmpty();
        }

        public FORMINFO BpoData2Xml( int runTid, MySqlDataReader dr, List<BpoPic> picList )
        {
            dataset = dr;
            flds = new Dictionary<string, int>();
            for ( int i = 0; i < dr.FieldCount; i++ )
            {
                var name = dr.GetName( i ).ToLower();
                if ( flds.ContainsKey( name ) )
                    nof.Add( "dupSqlCol: " + name );
                else
                    flds.Add( name, i );
            }

            // 1. top
            var form = new FORMINFO();

            form.FORMVERSION = dr_GetString( "formversion" );
            form.DOCID       = dr_GetString( "docid" );
            form.CASE_NO     = dr_GetString( "case_no" );
            form.VERSION     = dr_GetString( "version" );
            form.VENDOR      = dr_GetString( "vendor" );
            form.FILENUM     = dr_GetString( "filenum" );

            form.FORMNUM = dr_GetString( "formnum" ); ;

            // 2 subject

            #region 2. Subject

            var subj = new FORMINFOSUBJECT();

            subj.ADDR = new FORMINFOSUBJECTADDR();

            var addr = new FORMINFOSUBJECTADDR();

            addr.STREET    = dr_GetString( "subj_addr_street" );
            addr.CITY      = dr_GetString( "subj_addr_city" );
            addr.STATEPROV = dr_GetString( "subj_addr_stateprov" );
            addr.ZIP       = dr_GetString( "subj_addr_zip" );

            subj.ADDR = addr;

            subj.LOANNUM          = dr_GetString( "subj_loannum" );
            subj.BORROWER         = dr_GetString( "subj_borrower" );
            subj.CURRENTOWNER     = dr_GetString( "subj_currentowner" );
            subj.COUNTY           = dr_GetString( "subj_county" );
            subj.HOMEOWNERASSNFEE = dr_GetString( "subj_homeownerassnfee" );
            subj.CURRENTOCCUPANT  = dr_GetString( "subj_currentoccupant" );
            subj.PROPSECURE       = dr_GetString( "subj_propsecure" );
            subj.CURRENTUSE       = dr_GetString( "subj_currentuse" );
            subj.PROJECTEDUSE     = dr_GetString( "subj_projecteduse" );
            subj.DATASRC          = dr_GetString( "subj_datasrc" );

            var assesparcel = new FORMINFOSUBJECTASSESPARCEL();
            assesparcel.NUM = dr_GetString( "subj_parcel_num" );

            subj.ASSESPARCEL = assesparcel;

            var order = new FORMINFOSUBJECTORDER();
            order.NUM = dr_GetString( "subj_order_num" );

            subj.ORDER = order;

            var tracking = new FORMINFOSUBJECTTRACKING();
            tracking.NUM = dr_GetString( "subj_tracking_num" );

            subj.TRACKING = tracking;

            var proj = new FORMINFOSUBJECTPROJ();
            proj.TYPE = dr_GetString( "subj_proj_type" );
            proj.DESCRIPTION = dr_GetString( "subj_proj_type_other" );

            subj.PROJ = proj;

            var disaster = new FORMINFOSUBJECTDISASTER();
            disaster.RESPONSE = dr_GetString( "subj_disaster_response" );
            disaster.DATE = dr_GetString( "subj_disaster_date" );

            subj.DISASTER = disaster;

            var soldlisted = new FORMINFOSUBJECTSOLDLISTED();
            //soldlisted.VALUE = dr_GetString( "subj_sold" );
            soldlisted.PREVIOUSPRICE = dr_GetString( "subj_soldlisted_previousprice" );
            soldlisted.PREVIOUSDATE = dr_GetString( "subj_soldlisted_previousdate" );

            subj.SOLDLISTED = soldlisted;

            form.SUBJECT = subj;

            #endregion

            // 3. Review

            #region Review

            var review = new FORMINFOREVIEW();

            review.DESCRIPTION = dr_GetString( "review_description" );
            review.TYPE = dr_GetString( "review_type" );

            var repsec = new FORMINFOREVIEWREPORTSECTION();

            var fsubject = new FORMINFOREVIEWREPORTSECTIONSUBJECT();
            fsubject.COMMENTS = dr_GetString( "review_subj_comments" );

            repsec.SUBJECT = fsubject;

            var sechood = new FORMINFOREVIEWREPORTSECTIONNBRHOOD();
            sechood.DESCRIPTION = dr_GetString( "review_hood_description" );

            repsec.NBRHOOD = sechood;
            review.REPORTSECTION = repsec;

            var marketdata = new FORMINFOREVIEWMARKETDATA();

            var msubject = new FORMINFOREVIEWMARKETDATASUBJECT();
            msubject.LIVINGSQFT = dr_GetString( "review_livingsqft" );
            msubject.LOTSIZE = dr_GetString( "review_lotsize" );

            var subgrade = new FORMINFOREVIEWMARKETDATASUBJECTABOVEGRADE();
            subgrade.BEDROOMS = dr_GetString( "review_bedrooms" );

            msubject.ABOVEGRADE = subgrade;

            msubject.NUMUNITS = dr_GetString( "review_numunits" );
            msubject.PROPTYPE = dr_GetString( "review_proptype" );

            marketdata.SUBJECT = msubject;

            var compage = new FORMINFOREVIEWMARKETDATACOMPAGE();
            compage.RESPONSE = dr_GetString( "review_response" );

            marketdata.COMPAGE = compage;

            var sold6months = new FORMINFOREVIEWMARKETDATASOLD6MONTHS();
            sold6months.LISTPRICEAVG = dr_GetString( "review_listpriceavg6" );

            marketdata.SOLD6MONTHS = sold6months;

            var sold3months = new FORMINFOREVIEWMARKETDATASOLD3MONTHS();
            sold3months.LISTPRICEAVG = dr_GetString( "review_listpriceavg3" );

            marketdata.SOLD3MONTHS = sold3months;

            review.MARKETDATA = marketdata;

            form.REVIEW = review;

            #endregion

            // 4. LenderClient

            #region LenderClient

            var lenderclient = new FORMINFOLENDERCLIENT();

            var company = new FORMINFOLENDERCLIENTCOMPANY();
            company.NAME = dr_GetString( "lender_name" );

            lenderclient.COMPANY = company;

            var lcaddr = new FORMINFOLENDERCLIENTADDR();

            lcaddr.STREET = dr_GetString( "lender_street" );
            lcaddr.CITY = dr_GetString( "lender_city" );
            lcaddr.STATEPROV = dr_GetString( "lender_stateprov" );
            lcaddr.ZIP = dr_GetString( "lender_zip" );

            lenderclient.ADDR = lcaddr;

            form.LENDERCLIENT = lenderclient;

            #endregion


            // 5. Improvements

            #region Improvements

            var improvements = new FORMINFOIMPROVEMENTS();

            var general = new FORMINFOIMPROVEMENTSGENERAL();
            general.NEWCONSTRUCTION = dr_GetString( "improvements_newconstruction" );

            improvements.GENERAL = general;

            form.IMPROVEMENTS = improvements;

            #endregion

            // 6. ListingComps

            #region ListingComps

            var listComp = new FORMINFOLISTINGCOMP();

            var lcSubj = new FORMINFOLISTINGCOMPSUBJECT();

            lcSubj.MARKETDAYS          = dr_GetString( "lcs_marketdays" );
            lcSubj.ORIGINALPRICE       = dr_GetString( "lcs_originalprice" );
            lcSubj.ORIGINALLISTINGDATE = dr_GetString( "lcs_originallistingdate" );
            lcSubj.LISTINGPRICE        = dr_GetString( "lcs_listingprice" );
            lcSubj.REVISIONDATE        = dr_GetString( "lcs_revisiondate" );

            listComp.SUBJECT = lcSubj;

            listComp.NUM = dr_GetString( "lc_num" );

            var price = new FORMINFOLISTINGCOMPPRICE();
            price.LOW = dr_GetString( "lc_low" );
            price.HIGH = dr_GetString( "lc_high" );

            listComp.PRICE = price;

            var lcReps = new List<FORMINFOLISTINGCOMPREPAIR>();
            for ( int i = 0; i < 10; i++ )
            {
                var rep = new FORMINFOLISTINGCOMPREPAIR(); // <REPAIR NUM="1">

                string k = "lc_rep" + ( i + 1 ).ToString() + "_";

                rep.ITEM          = dr_GetString( k + "item" );
                rep.RESPONSE      = dr_GetString( k + "response" );
                rep.ESTCOST       = dr_GetString( k + "estcost" );
                if ( i == 0 )
                    rep.TOTALCOST = dr_GetString( k + "totalcost" );
                rep.NUM           = Convert.ToByte( i + 1 );

                lcReps.Add( rep );
            }
            listComp.REPAIR = lcReps.ToArray();

            var lcComps = new List<FORMINFOLISTINGCOMPLISTCOMPS>();

            for ( int i = 0; i < 3; i++ )
            {
                var lc = new FORMINFOLISTINGCOMPLISTCOMPS(); // <LISTCOMPS COMPNUM="1">

                lc.COMPNUM = Convert.ToByte( i + 1 );

                var lcAddr = new FORMINFOLISTINGCOMPLISTCOMPSADDR();

                string k = "lc" + ( i + 1 ).ToString() + "_";

                lcAddr.STREET = dr_GetString( k + "street" );
                lcAddr.CITY   = dr_GetString( k + "city" );
                lcAddr.ZIP    = dr_GetString( k + "zip" );

                lc.ADDR = lcAddr;

                lc.DATA          = dr_GetString( k + "data" );
                lc.PROXIMITY     = dr_GetString( k + "proximity" );
                lc.ORGLISTINGDT  = dr_GetString( k + "orglistingdt" );
                lc.ORIGINALPRICE = dr_GetString( k + "originalprice" );
                lc.LISTINGPRICE  = dr_GetString( k + "listingprice" );
                lc.MARKETDAYS    = dr_GetString( k + "marketdays" );
                lc.TOTDAYSMARKET = dr_GetString( k + "totdaysmarket" );

                var lcFunds = new FORMINFOLISTINGCOMPLISTCOMPSSRCFUNDS();
                lcFunds.DESCRIPTION = dr_GetString( k + "srcfunds_description" );

                lc.SRCFUNDS = lcFunds;

                var lcConsess = new FORMINFOLISTINGCOMPLISTCOMPSCONCESSIONS();

                var lcSales = new FORMINFOLISTINGCOMPLISTCOMPSCONCESSIONSSALES();
                lcSales.DESCRIPTION = dr_GetString( k + "sales_description" );

                lcConsess.SALES = lcSales;

                lc.CONCESSIONS = lcConsess;

                lc.CONCESSIONS.SALES = lcSales;

                lc.DISTRESSEDSALE = dr_GetString( k + "distressedsale" );
                lc.LOTSIZE        = dr_GetString( k + "lotsize" );
                lc.SITE           = dr_GetString( k + "site" );
                lc.VIEW           = dr_GetString( k + "view" );
                lc.VIEWCOMPARISON = dr_GetString( k + "viewcomparison" );
                lc.PROPTYPE       = dr_GetString( k + "proptype" );
                lc.UNITNUM        = dr_GetString( k + "unitnum" );
                lc.CONSTRUCT      = dr_GetString( k + "construct" );
                lc.DESIGNSTYLE    = dr_GetString( k + "designstyle" );
                lc.AGEYRS         = dr_GetString( k + "ageyrs" );
                lc.CONDITION      = dr_GetString( k + "condition" );
                lc.LIVINGSQFT     = dr_GetString( k + "livingsqft" );

                var lcAg = new FORMINFOLISTINGCOMPLISTCOMPSABOVEGRADE();

                lcAg.ROOMTOT  = dr_GetString( k + "roomtot" );
                lcAg.BEDROOMS = dr_GetString( k + "bedrooms" );
                lcAg.BATHFULL = dr_GetString( k + "bathfull" );
                lcAg.BATHHALF = dr_GetString( k + "bathhalf" );

                lc.ABOVEGRADE     = lcAg;
                lc.BASEMENTTYPE   = dr_GetString( k + "basementtype" );
                lc.BASEMENT       = dr_GetString( k + "basement" );
                lc.BASEMENTFINISH = dr_GetString( k + "basementfinish" );
                lc.GARAGE         = dr_GetString( k + "garage" );
                lc.GARAGENUMCARS  = dr_GetString( k + "garagenumcars" );
                lc.POOL           = dr_GetString( k + "pool" );
                lc.OTHERAMENITIES = dr_GetString( k + "otheramenities" );

                var lcHoa = new FORMINFOLISTINGCOMPLISTCOMPSHOAASSESSMENT();
                lcHoa.DESCRIPTION = dr_GetString( k + "hoa_description" );

                lc.HOAASSESSMENT = lcHoa;

                var lcMc = new FORMINFOLISTINGCOMPLISTCOMPSMOSTCOMPARABLE();
                lcMc.RESPONSE = dr_GetString( k + "response" );

                lc.MOSTCOMPARABLE = lcMc;

                lcComps.Add( lc );
            }

            listComp.LISTCOMPS = lcComps.ToArray();

            listComp.ANALYSISLISTINGDATA = dr_GetString( "lc_analysislistingdata" );
            listComp.ANALYSISLISTINGDATA2 = dr_GetString( "lc_analysislistingdata2" );
            listComp.ANALYSISLISTINGDATA3 = dr_GetString( "lc_analysislistingdata3" );

            form.LISTINGCOMP = listComp;

            #endregion

            // 7. Site

            #region Site

            var site = new FORMINFOSITE();

            var saleshistory = new FORMINFOSITESALESHISTORY();
            saleshistory.RESPONSE = dr_GetString( "subj_sold" );

            site.SALESHISTORY = saleshistory;

            var zoning = new FORMINFOSITEZONING();
            zoning.SPECIFIC = dr_GetString( "site_specific" );
            zoning.DESCRIPTION = dr_GetString( "site_description" );

            site.ZONING = zoning;

            form.SITE = site;

            #endregion

            // 8. ListBroker

            #region ListBroker

            var lb = new FORMINFOLISTBROKER();
            lb.NAME = dr_GetString( "lb_name" );

            var lbCo = new FORMINFOLISTBROKERCOMPANY();
            lbCo.NAME = dr_GetString( "lb_co_name" );

            lb.COMPANY = lbCo;
            lb.PHONE = dr_GetString( "lb_phone" ).JustInt( "" ).FormatPhone();

            form.LISTBROKER = lb;

            #endregion

            // 9. Report

            #region Report

            var report = new FORMINFOREPORT();

            report.DATE = dr_GetString( "report_date" );

            form.REPORT = report;

            #endregion

            // 10. NbrHood

            #region NbrHood

            var hood = new FORMINFONBRHOOD();

            hood.LOCATION      = dr_GetString( "hood_location" );
            hood.PROPVALUES    = dr_GetString( "hood_propvalues" );
            hood.MARKETINGTIME = dr_GetString( "hood_marketingtime" );

            var sum = new FORMINFONBRHOODHOUSINGSUMMARY();
            sum.MEDIANRENT = dr_GetString( "hood_medianrent" );

            hood.HOUSINGSUMMARY = sum;

            hood.MARKETINGTIMEDESCRIPTION = dr_GetString( "hood_marketingtimedescription" );

            var occ = new FORMINFONBRHOODOCCUPANCY();

            occ.OWNER = new FORMINFONBRHOODOCCUPANCYOWNER();
            occ.OWNER.OWNER = dr_GetString( "hood_owner" ); // <OWNER OWNER="OWNER"/>

            var vac = new FORMINFONBRHOODOCCUPANCYVACANT();
            vac.PERCENT = dr_GetString( "hood_percent" );

            occ.VACANT = vac;

            hood.OCCUPANCY = occ;

            var landuse = new FORMINFONBRHOODLANDUSE();
            landuse.INDUSTRIAL = dr_GetString( "hood_industrial" );

            hood.LANDUSE = landuse;

            hood.NEWCONSTRUCTION = dr_GetString( "hood_newconstruction" );

            var hoodDis = new FORMINFONBRHOODDISASTER();
            hoodDis.RESPONSE = dr_GetString( "hood_response" );
            hoodDis.DATE = dr_GetString( "hood_date" );

            hood.DISASTER = hoodDis;

            hood.MARKETCONDITIONS = dr_GetString( "hood_marketconditions" );

            var csp = new FORMINFONBRHOODCLOSEDSALESPAST();
            csp.NUM = dr_GetString( "hood_csp_num" );

            hood.CLOSEDSALESPAST = csp;

            var market = new FORMINFONBRHOODMARKET();

            var absorptionrate = new FORMINFONBRHOODMARKETABSORPTIONRATE();
            absorptionrate.LAST4TO6MOS = dr_GetString( "hood_absorb_last4to6mos" );

            market.ABSORPTIONRATE = absorptionrate;

            var mediansalesprice = new FORMINFONBRHOODMARKETMEDIANSALESPRICE();
            mediansalesprice.LAST4TO6MOS = dr_GetString( "hood_median_last4to6mos" );
            mediansalesprice.LAST3MONTHS = dr_GetString( "hood_median_last3months" );

            market.MEDIANSALESPRICE = mediansalesprice;

            var s2lRation = new FORMINFONBRHOODMARKETSALESTOLISTRATIO();
            s2lRation.LAST4TO6MOS = dr_GetString( "hood_sales_last4to6mos" );
            s2lRation.LAST3MONTHS = dr_GetString( "hood_sales_last3months" );

            market.SALESTOLISTRATIO = s2lRation;

            hood.MARKET = market;

            var pendingsales = new FORMINFONBRHOODPENDINGSALES();
            pendingsales.NUM = dr_GetString( "hood_pending_num" );

            hood.PENDINGSALES = pendingsales;
            hood.DEMANDSUPPLY = dr_GetString( "hood_demandsupply" );
            hood.MARKET = market;

            form.NBRHOOD = hood;

            #endregion

            // 11. Comment

            #region Comment

            var fiComment = new FORMINFOCOMMENT();
            fiComment.REOSALESEFFECT = dr_GetString( "comment_reosaleseffect" );
            fiComment.COMMENTS = dr_GetString( "comment_comments" );

            form.COMMENT = fiComment;

            #endregion

            // 12. SalesComp

            #region SalesComp

            var salesComp = new FORMINFOSALESCOMP();

            var cs = new FORMINFOSALESCOMPCOMPSALES();
            cs.NUM = dr_GetString( "sc_num" );

            var csPrice = new FORMINFOSALESCOMPCOMPSALESPRICE();
            csPrice.LOW = dr_GetString( "sc_price_low" );
            csPrice.HIGH = dr_GetString( "sc_price_high" );

            cs.PRICE = csPrice;
            cs.RADIUS = dr_GetString( "sc_radius" );

            salesComp.COMPSALES = cs;

            var csSubj = new FORMINFOSALESCOMPSUBJECT();

            csSubj.YRBUILT = dr_GetString( "scs_yrbuilt" );

            var scSubjAg = new FORMINFOSALESCOMPSUBJECTABOVEGRADE();
            scSubjAg.BATH     = dr_GetString( "scs_scbath" );
            scSubjAg.ROOMTOT  = dr_GetString( "scs_roomtot" );
            scSubjAg.BEDROOMS = dr_GetString( "scs_bedrooms" );
            scSubjAg.BATHFULL = dr_GetString( "scs_bathfull" );
            scSubjAg.BATHHALF = dr_GetString( "scs_bathhalf" );

            csSubj.ABOVEGRADE = scSubjAg;

            var scSubjAddr = new FORMINFOSALESCOMPSUBJECTADDR();
            scSubjAddr.STREET = dr_GetString( "scs_street" );
            scSubjAddr.CITY   = dr_GetString( "scs_city" );
            scSubjAddr.ZIP    = dr_GetString( "scs_zip" );

            csSubj.ADDR = scSubjAddr;

            csSubj.ORIGINALLISTDT    = dr_GetString( "scs_originallistdt" );
            csSubj.ORIGINALLISTPRICE = dr_GetString( "scs_originallistprice" );
            csSubj.CURRENTLISTPRICE  = dr_GetString( "scs_currentlistprice" );
            csSubj.DAYSONMARKET      = dr_GetString( "scs_daysonmarket" );
            csSubj.TOTDAYSONMARKET   = dr_GetString( "scs_totdaysonmarket" );
            csSubj.SRCFUNDS          = dr_GetString( "scs_srcfunds" );

            var subConcess = new FORMINFOSALESCOMPSUBJECTCONCESSIONS();
            subConcess.SALES = dr_GetString( "scs_sales" );

            csSubj.CONCESSIONS = subConcess;

            csSubj.DISTRESSEDSALE = dr_GetString( "scs_distressedsale" );
            csSubj.LOTSIZE        = dr_GetString( "scs_lotsize" );
            csSubj.SITE           = dr_GetString( "scs_site" );
            csSubj.VIEW           = dr_GetString( "scs_view" );
            csSubj.PROPTYPE       = dr_GetString( "scs_proptype" );
            csSubj.NUMUNITS       = dr_GetString( "scs_numunits" );
            csSubj.CONSTRUCT      = dr_GetString( "scs_construct" );
            csSubj.DESIGNSTYLE    = dr_GetString( "scs_designstyle" );
            csSubj.AGEYRS         = dr_GetString( "scs_ageyrs" );
            csSubj.CONDITION      = dr_GetString( "scs_condition" );
            csSubj.LIVINGSQFT     = dr_GetString( "scs_livingsqft" );
            csSubj.BASEMENTTYPE   = dr_GetString( "scs_basementtype" );
            csSubj.BASEMENT       = dr_GetString( "scs_basement" );
            csSubj.BASEMENTFINISH = dr_GetString( "scs_basementfinish" );
            csSubj.GARAGE         = dr_GetString( "scs_garage" );
            csSubj.GARAGENUMCARS  = dr_GetString( "scs_garagenumcars" );
            csSubj.POOL           = dr_GetString( "scs_pool" );
            csSubj.AMENITIES      = dr_GetString( "scs_amenities" );
            csSubj.HOAASSESSMENT  = dr_GetString( "scs_hoaassessment" );

            salesComp.SUBJECT = csSubj;

            var scComps = new List<FORMINFOSALESCOMPCOMPS>();

            for ( int i = 0; i < 3; i++ )
            {
                var comp = new FORMINFOSALESCOMPCOMPS(); // <SALESCOMPS COMPNUM="1">

                comp.COMPNUM = Convert.ToByte( i + 1 );

                string k = "sc" + ( i + 1 ).ToString() + "_";

                var lcAddr = new FORMINFOSALESCOMPCOMPSADDR();
                lcAddr.STREET = dr_GetString( k + "street" );
                lcAddr.CITY   = dr_GetString( k + "city" );
                lcAddr.ZIP    = dr_GetString( k + "zip" );

                comp.ADDR              = lcAddr;
                comp.DATA              = dr_GetString( k + "data" );
                comp.PROXIMITY         = dr_GetString( k + "proximity" );
                comp.ORIGINALLISTINGDT = dr_GetString( k + "orglistingdt" );
                comp.ORIGINALPRICE     = dr_GetString( k + "originalprice" );
                comp.LISTPRICE         = dr_GetString( k + "listprice" );

                var scSale = new FORMINFOSALESCOMPCOMPSSALE();
                scSale.DATE = dr_GetString( k + "date" );

                comp.SALE = scSale;
                comp.SALESPRICE = dr_GetString( k + "salesprice" );
                comp.MARKETDAYS = dr_GetString( k + "marketdays" );
                comp.TOTMARKETDAYS = dr_GetString( k + "totmarketdays" );

                var scFunds = new FORMINFOSALESCOMPCOMPSSRCFUNDS();
                scFunds.DESCRIPTION = dr_GetString( k + "srcefunds_description" );

                comp.SRCFUNDS = scFunds;

                var scConsess = new FORMINFOSALESCOMPCOMPSCONCESSIONS();

                var scSales = new FORMINFOSALESCOMPCOMPSCONCESSIONSSALES();
                scSales.DESCRIPTION = dr_GetString( k + "concess_description" );

                scConsess.SALES = scSales;

                comp.CONCESSIONS = scConsess;
                comp.DISTRESSEDSALE = dr_GetString( k + "distressedsale" );
                comp.LOTSIZE = dr_GetString( k + "lotsize" );

                var scSite = new FORMINFOSALESCOMPCOMPSSITE();
                scSite.DESCRIPTION = dr_GetString( k + "site_description" );

                comp.SITE = scSite;

                var view           = new FORMINFOSALESCOMPCOMPSVIEW();
                var viewcomparison = new FORMINFOSALESCOMPCOMPSVIEWCOMPARISON();
                var proptype       = new FORMINFOSALESCOMPCOMPSPROPTYPE();
                var numunits       = new FORMINFOSALESCOMPCOMPSNUMUNITS();
                var construct      = new FORMINFOSALESCOMPCOMPSCONSTRUCT();
                var designstyle    = new FORMINFOSALESCOMPCOMPSDESIGNSTYLE();
                var ay             = new FORMINFOSALESCOMPCOMPSAGEYRS();
                var condition      = new FORMINFOSALESCOMPCOMPSCONDITION();
                var livingsqft     = new FORMINFOSALESCOMPCOMPSLIVINGSQFT();

                view.DESCRIPTION           = dr_GetString( k + "view_description" );
                viewcomparison.DESCRIPTION = dr_GetString( k + "viewcomparison_description" );
                proptype.DESCRIPTION       = dr_GetString( k + "proptype_description" );
                numunits.DESCRIPTION       = dr_GetString( k + "numunits_description" );
                construct.DESCRIPTION      = dr_GetString( k + "construct_description" );
                designstyle.DESCRIPTION    = dr_GetString( k + "design_description" );
                ay.AGEYRS                  = dr_GetString( k + "ageyrs" );
                condition.DESCRIPTION      = dr_GetString( k + "condition_description" );
                livingsqft.SQFT            = dr_GetString( k + "living_sqft" );

                comp.VIEW           = view;
                comp.VIEWCOMPARISON = viewcomparison;
                comp.PROPTYPE       = proptype;
                comp.NUMUNITS       = numunits;
                comp.CONSTRUCT      = construct;
                comp.DESIGNSTYLE    = designstyle;
                comp.AGEYRS         = ay;
                comp.CONDITION      = condition;
                comp.LIVINGSQFT     = livingsqft;

                var lcAg      = new FORMINFOSALESCOMPCOMPSABOVEGRADE();
                lcAg.ROOMTOT  = dr_GetString( k + "roomtot" );
                lcAg.BEDROOMS = dr_GetString( k + "bedrooms" );
                lcAg.BATHFULL = dr_GetString( k + "bathfull" );
                lcAg.BATHHALF = dr_GetString( k + "bathhalf" );

                comp.ABOVEGRADE = lcAg;

                var basementtype   = new FORMINFOSALESCOMPCOMPSBASEMENTTYPE();
                var basement       = new FORMINFOSALESCOMPCOMPSBASEMENT();
                var basementfinish = new FORMINFOSALESCOMPCOMPSBASEMENTFINISH();
                var garage         = new FORMINFOSALESCOMPCOMPSGARAGE();
                var pool           = new FORMINFOSALESCOMPCOMPSPOOL();
                var amenities      = new FORMINFOSALESCOMPCOMPSAMENITIES();
                var hoaassessment  = new FORMINFOSALESCOMPCOMPSHOAASSESSMENT();
                var mostcomparable = new FORMINFOSALESCOMPCOMPSMOSTCOMPARABLE();

                basementtype.DESCRIPTION   = dr_GetString( k + "basementtype_description" );
                basement.DESCRIPTION       = dr_GetString( k + "basement_description" );
                basementfinish.DESCRIPTION = dr_GetString( k + "basementfinish_description" );
                garage.DESCRIPTION         = dr_GetString( k + "garage_description" );
                garage.NUMCARS             = dr_GetString( k + "garage_numcars" );
                pool.DESCRIPTION           = dr_GetString( k + "pool_description" );
                amenities.DESCRIPTION      = dr_GetString( k + "amenities_description" );
                hoaassessment.DESCRIPTION  = dr_GetString( k + "hoa_description" );
                mostcomparable.RESPONSE    = dr_GetString( k + "mostcomp_response" );

                comp.BASEMENTTYPE   = basementtype;
                comp.BASEMENT       = basement;
                comp.BASEMENTFINISH = basementfinish;
                comp.GARAGE         = garage;
                comp.POOL           = pool;
                comp.AMENITIES      = amenities;
                comp.HOAASSESSMENT  = hoaassessment;

                comp.MOSTCOMPARABLE = mostcomparable;

                scComps.Add( comp );
            }

            salesComp.COMPS = scComps.ToArray();
            salesComp.COMMENTSALE1 = dr_GetString( "sc_commentsale1" );
            salesComp.COMMENTSALE2 = dr_GetString( "sc_commentsale2" );
            salesComp.COMMENTSALE3 = dr_GetString( "sc_commentsale3" );

            form.SALESCOMP = salesComp;

            #endregion

            // 13. Reconcil

            #region Reconcil

            var reconcil = new FORMINFORECONCIL();

            var redflag = new FORMINFORECONCILREDFLAG();

            redflag.DAMAGED = dr_GetString( "recon_damaged" );
            redflag.CONSTRUCTION = dr_GetString( "recon_construction" );
            redflag.ENVIRONMENTAL = dr_GetString( "recon_environmental" );
            redflag.ZONING = dr_GetString( "recon_zoning" );
            redflag.MARKETACTIVITY = dr_GetString( "recon_marketactivity" );
            redflag.BOARDED = dr_GetString( "recon_boarded" );
            redflag.STIGMA = dr_GetString( "recon_stigma" );
            redflag.OTHER = dr_GetString( "recon_other" );
            redflag.COMMENTS = dr_GetString( "recon_comments" );

            reconcil.REDFLAG = redflag;

            var estmarketvalue = new FORMINFORECONCILESTMARKETVALUE();

            var quickmkttime = new FORMINFORECONCILESTMARKETVALUEQUICKMKTTIME();
            var qasis = new FORMINFORECONCILESTMARKETVALUEQUICKMKTTIMEASIS();
            qasis.LISTAMT = dr_GetString( "recon_qt_listamt" );
            qasis.AMT = dr_GetString( "recon_qt_amt" );

            quickmkttime.ASIS = qasis;
            estmarketvalue.QUICKMKTTIME = quickmkttime;

            var normalmkttime = new FORMINFORECONCILESTMARKETVALUENORMALMKTTIME();
            var nasis = new FORMINFORECONCILESTMARKETVALUENORMALMKTTIMEASIS();
            nasis.DAYS = dr_GetString( "recon_asis_days" );
            nasis.LISTAMT = dr_GetString( "recon_asis_listamt" );
            nasis.AMT = dr_GetString( "recon_asis_amt" );

            normalmkttime.ASIS = nasis;

            var repaired = new FORMINFORECONCILESTMARKETVALUENORMALMKTTIMEREPAIRED();

            repaired.DAYS = dr_GetString( "recon_rep_days" );
            repaired.LISTAMT = dr_GetString( "recon_rep_listamt" );
            repaired.AMT = dr_GetString( "recon_rep_amt" );

            normalmkttime.REPAIRED = repaired;
            estmarketvalue.NORMALMKTTIME = normalmkttime;

            reconcil.ESTMARKETVALUE = estmarketvalue;

            form.RECONCIL = reconcil;

            #endregion

            // 14. Addendums

            #region Addendums

            var addendums = new FORMINFOADDENDUMS();

            var signature = new FORMINFOADDENDUMSSIGNATURE();

            var sappraiser = new FORMINFOADDENDUMSSIGNATUREAPPRAISER();
            sappraiser.FILENAME = "";
            sappraiser.FILETYPE = "";

            signature.APPRAISER = sappraiser;

            // 14.Subject pics
            var fv = picList.Where( w=> w.slot == "frontview").FirstOrDefault();
            var rv = picList.Where( w => w.slot == "rearview" ).FirstOrDefault();
            var sv = picList.Where( w => w.slot == "streetview" ).FirstOrDefault();
            var b1 = 1.ToByte();
            var b2 = 2.ToByte();
            var b3 = 3.ToByte();
            var b4 = 4.ToByte();
            
            var subjPhotos = new FORMINFOADDENDUMSSUBJECTPHOTO();
            if ( fv != null )
            {
                fv.addedToXml = true;
                subjPhotos.FRONTVIEW = new FORMINFOADDENDUMSSUBJECTPHOTOFRONTVIEW()
                {
                    FILENAME = fv.fileNameFnc,
                    FILETYPE = fv.fileType,
                    DESCRIPTION = new FORMINFOADDENDUMSSUBJECTPHOTOFRONTVIEWDESCRIPTION[] {
                        new FORMINFOADDENDUMSSUBJECTPHOTOFRONTVIEWDESCRIPTION { NUM = b1, Value = fv.description1 },
                        new FORMINFOADDENDUMSSUBJECTPHOTOFRONTVIEWDESCRIPTION { NUM = b2, Value = fv.description2 },
                        new FORMINFOADDENDUMSSUBJECTPHOTOFRONTVIEWDESCRIPTION { NUM = b3, Value = fv.description3 },
                        new FORMINFOADDENDUMSSUBJECTPHOTOFRONTVIEWDESCRIPTION { NUM = b4, Value = fv.dateTaken },
                    }
                };
            };
            if ( rv != null )
            {
                rv.addedToXml = true;
                subjPhotos.REARVIEW = new FORMINFOADDENDUMSSUBJECTPHOTOREARVIEW()
                {
                    FILENAME = rv.fileNameFnc,
                    FILETYPE = rv.fileType,
                    DESCRIPTION = new FORMINFOADDENDUMSSUBJECTPHOTOREARVIEWDESCRIPTION[] {
                        new FORMINFOADDENDUMSSUBJECTPHOTOREARVIEWDESCRIPTION { NUM = b1, Value = rv.description1 },
                        new FORMINFOADDENDUMSSUBJECTPHOTOREARVIEWDESCRIPTION { NUM = b2, Value = rv.description2 },
                        new FORMINFOADDENDUMSSUBJECTPHOTOREARVIEWDESCRIPTION { NUM = b3, Value = rv.description3 },
                        new FORMINFOADDENDUMSSUBJECTPHOTOREARVIEWDESCRIPTION { NUM = b4, Value = rv.dateTaken },
                    }
                };
            }
            if( sv != null )
            {
                sv.addedToXml = true;
                subjPhotos.STREETVIEW = new FORMINFOADDENDUMSSUBJECTPHOTOSTREETVIEW()
                {
                    FILENAME = sv.fileNameFnc,
                    FILETYPE = sv.fileType,
                    DESCRIPTION = new FORMINFOADDENDUMSSUBJECTPHOTOSTREETVIEWDESCRIPTION[] {
                        new FORMINFOADDENDUMSSUBJECTPHOTOSTREETVIEWDESCRIPTION { NUM = b1, Value = sv.description1 },
                        new FORMINFOADDENDUMSSUBJECTPHOTOSTREETVIEWDESCRIPTION { NUM = b2, Value = sv.description2 },
                        new FORMINFOADDENDUMSSUBJECTPHOTOSTREETVIEWDESCRIPTION { NUM = b3, Value = sv.description3 },
                        new FORMINFOADDENDUMSSUBJECTPHOTOSTREETVIEWDESCRIPTION { NUM = b4, Value = sv.dateTaken },
                    }
                };
            }

            addendums.SUBJECTPHOTO = subjPhotos;

            // 14.Additional pics

            var addPhotos = new FORMINFOADDENDUMSADDITIONALPHOTOS();
            var addPhotoList = new List<FORMINFOADDENDUMSADDITIONALPHOTOSPHOTO>();
            int kp = 0;
            foreach ( var item in picList.Where( w => w.slot == "additional" ) )
            {
                item.addedToXml = true;
                int kd = 0;
                addPhotoList.Add( new FORMINFOADDENDUMSADDITIONALPHOTOSPHOTO()
                {
                    NUM = ( ++kp ).ToByte(),
                    FILENAME = item.fileNameFnc,
                    FILETYPE = item.fileType,
                    DESCRIPTION = new FORMINFOADDENDUMSADDITIONALPHOTOSPHOTODESCRIPTION[] {
                        new FORMINFOADDENDUMSADDITIONALPHOTOSPHOTODESCRIPTION { NUM = (++kd).ToByte(), Value = item.description1 },
                        new FORMINFOADDENDUMSADDITIONALPHOTOSPHOTODESCRIPTION { NUM = (++kd).ToByte(), Value = item.description2 },
                        new FORMINFOADDENDUMSADDITIONALPHOTOSPHOTODESCRIPTION { NUM = (++kd).ToByte(), Value = item.description3 },
                        new FORMINFOADDENDUMSADDITIONALPHOTOSPHOTODESCRIPTION { NUM = (++kd).ToByte(), Value = item.dateTaken },
                    }
                } );
            }
            addPhotos.Items = addPhotoList.ToArray();
            addendums.ADDITIONALPHOTOS = addPhotos;

            // 14.Comp Sale pics

            var compSalePhotoList = new List<FORMINFOADDENDUMSCOMPPHOTO>();
            kp = 0;
            foreach ( var item in picList.Where( w => w.slot.StartsWith("sale") ) )
            {
                item.addedToXml = true;
                compSalePhotoList.Add( new FORMINFOADDENDUMSCOMPPHOTO()
                {                    
                    COMPNUM = ( ++kp ).ToByte(),
                    FILENAME = item.fileNameFnc,
                    FILETYPE = item.fileType,
                } );
            }

            addendums.COMPPHOTO = compSalePhotoList.ToArray();

            // 14.Comp List pics

            var compListPhotoList = new List<FORMINFOADDENDUMSLISTINGCOMPPHOTO>();
            kp = 0;
            foreach ( var item in picList.Where( w => w.slot.StartsWith( "list" ) ) )
            {
                item.addedToXml = true;
                int kd = 0;
                compListPhotoList.Add( new FORMINFOADDENDUMSLISTINGCOMPPHOTO()
                {
                    COMPNUM = ( ++kp ).ToByte(),
                    FILENAME = item.fileNameFnc,
                    FILETYPE = item.fileType,
                    DESCRIPTION = new FORMINFOADDENDUMSLISTINGCOMPPHOTODESCRIPTION[] {
                        new FORMINFOADDENDUMSLISTINGCOMPPHOTODESCRIPTION { NUM = (++kd).ToByte(), Value = item.description1 },
                        new FORMINFOADDENDUMSLISTINGCOMPPHOTODESCRIPTION { NUM = (++kd).ToByte(), Value = item.description2 },
                        new FORMINFOADDENDUMSLISTINGCOMPPHOTODESCRIPTION { NUM = (++kd).ToByte(), Value = item.description3 },
                        new FORMINFOADDENDUMSLISTINGCOMPPHOTODESCRIPTION { NUM = (++kd).ToByte(), Value = item.dateTaken },
                    }

                } );
            }

            addendums.LISTINGCOMPPHOTO = compListPhotoList.ToArray();

            form.ADDENDUMS = addendums;

            #endregion

            // 16. Broker

            #region Broker

            var broker = new FORMINFOBROKER();

            broker.NAME = dr_GetString( "bkr_name" );
            var license = new FORMINFOBROKERLICENSE();
            license.NUM = dr_GetString( "bkr_num" );
            license.STATEPROV = dr_GetString( "bkr_stateprov" );

            broker.LICENSE = license;

            var bcompany = new FORMINFOBROKERCOMPANY();
            bcompany.NAME = dr_GetString( "bkr_co_name" );
            bcompany.ADDR = dr_GetString( "bkr_co_addr" );

            broker.COMPANY = bcompany;
            broker.PHONE = dr_GetString( "bkr_phone" ).JustInt( "" ).FormatPhone();

            form.BROKER = broker;

            #endregion

            // 17. Appraiser

            #region Appriaser

            var appraiser = new FORMINFOAPPRAISER();

            var signed = new FORMINFOAPPRAISERSIGNED();
            signed.DATE = dr_GetString( "apr_date" );

            appraiser.SIGNED = signed;

            form.APPRAISER = appraiser;

            #endregion

            // following s/not happend.  all dbCols -> xmlFields s/be accounted for
            if ( nof != null && nof.Count > 0 )
            {
                string fname = reqFileName.FormatWith( runTid, DateTime.Now.ToString( "yyyyMMdd_HHmmsss" ) );
                string tmpFile = Path.Combine( config.tempFileDir, fname );

                if ( config.saveTempFiles )
                    File.WriteAllText( tmpFile + ".nof", String.Join( "\n", nof.ToArray() ) );
            }
            return form;
        }

        private const string reqFileName = "CMSPdfEnv_{0}_{1}_resquest.xml";
        private const string respFileName = "CMSPdfEnv_{0}_{1}_response.xml";

        public SendResults PostBpoRequest( int runTid, FORMINFO cmsBpo, List<BpoPic> picList = null )
        {
            var results = new SendResults()
            {
                isSubmitOk = false,
                submitError = "start sending"
            };

            string fname = respFileName.FormatWith( DateTime.Now.ToString( "yyyyMMdd_HHmmsss" ), runTid );
            string responseFile = Path.Combine( config.tempFileDir, fname );

            if ( File.Exists( responseFile ) ) File.Delete( responseFile );

            string err        = null;
            this.responseFile = responseFile;
            this.runTid       = runTid;
            this.cmsBpo       = cmsBpo;
            this.picList      = picList;

            if ( wp.SaveFileFromUrl( responseFile, out err, PrepBpoRequest, maxFileSize: config.maxFilesize ) )
            {
                try  //success;  get tmpfile.  deserialize it
                {
                    results.submitError = "ResponseFile not Deserialized: " + responseFile;
                    var resp = File.ReadAllText( responseFile ).Deserialize<ResNet.Bpo.CMS.RESPONSE>();
                    if ( resp == null )
                    {
                        log.Error( results.submitError );
                    }
                    else
                    {
                        results.isSubmitOk = resp != null && resp.CONFIRMATION != null;
                        if ( results.isSubmitOk )
                            results.submitError = "";
                        results.response = resp;
                    }
                }
                catch ( Exception e )
                {
                    results.submitError = "ResponseFile not Deserialized: " + e.Message;
                    log.Error( results.submitError );
                }
            }
            else
            {
                results.submitError = "SaveFileFormUrl download failed: {0} url: {1} saveTo: {2}".FormatWith( err, config.url, responseFile );
            }

            if ( ! config.saveTempFiles && File.Exists( responseFile ) ) File.Delete( responseFile );

            return results;
        }

        private HttpWebRequest PrepBpoRequest()
        {
            const string hdr = "{0}: {1}";
            const string fdName = "form-data; name=\"{0}\"";
            const string fdNameFile = "form-data; name=\"{0}\"; filename=\"{1}\"";
            const string contentTypeXml = "text/xml";
            const string contentTypePic = "application/octet-stream";
            const string contentDisp = "Content-Disposition";
            const string contentType = "Content-Type";

            // 1. save request while testing

            var postData = new HttpPostMimeParts();
            postData.AddPart( config.userId, hdr.FormatWith( contentDisp, fdName.FormatWith( "uid" ) ) );
            postData.AddPart( config.passwd, hdr.FormatWith( contentDisp, fdName.FormatWith( "pwd" ) ) );
            postData.AddPart( cmsBpo.SerializeObject<FORMINFO>()
                    , hdr.FormatWith( contentDisp, fdNameFile.FormatWith( "bpoxmlfile", "BPO.xml" ) )
                    , hdr.FormatWith( contentType, contentTypeXml )
                );

            // 2. add pics to mime list

            var sb = new StringBuilder();
            if ( picList == null ) picList = new List<BpoPic>();

            foreach ( var pic in picList )
            {
                if ( !pic.addedToXml ) continue;

                string pf = Path.Combine( pic.fileDir, pic.fileName );

                try
                {
                    postData.AddPart( File.ReadAllBytes( pf )
                        , hdr.FormatWith( contentDisp, fdNameFile.FormatWith( pic.fileNameFnc, pic.fileNameFnc ) )
                        , hdr.FormatWith( contentType, contentTypePic )
                        );

                }
                catch ( Exception e )
                {
                    sb.AppendFormat( "; readFile: {0}, {1}", pic.fileNameFnc, e.Message );
                    continue;
                }
            }

            if ( sb.Length > 0 )
            {
                log.ErrorFormat( "Some Files not uploaded:" + sb.ToString().Substring( 1 ) );
            }

            // 3. setup webRequest

            var request = ( HttpWebRequest ) WebRequest.Create( config.url );

            request.Method = "POST";
            request.AllowAutoRedirect = false;
            request.Timeout = config.requestTimeoutMs;
            request.Accept = config.requestAccept;
            request.MediaType = config.requestMediaType;
            request.ContentType = postData.ContentType;
            request.ContentLength = postData.ContentLength;

            postData.SetStream( request );

            // 4. save request while testing

            if ( config.saveTempFiles )
            {
                postData.LogRequestFile( responseFile.Replace( "response", "request" ) );
            }

            return request;

        }

        public bool GetFile( string url, string saveToFilename, out string errMsg )
        {
            errMsg = "";
            string err = null;

            Func<HttpWebRequest> callBack = ( () =>
            {
                var request = ( HttpWebRequest ) WebRequest.Create( url );
                request.Method            = "GET";
                request.AllowAutoRedirect = false;
                request.Timeout           = config.requestTimeoutMs;
                request.Accept            = config.requestAccept;
                request.ContentType       = config.requestContentType;
                request.MediaType         = config.requestMediaType;

                return request;
            } );

            if ( wp.SaveToFileNameFromUrl( saveToFilename, out err, callBack, maxFileSize: config.maxFilesize ) )
            {
                return File.Exists( saveToFilename );
            }
            else
            {
                errMsg = "SaveToFileNameFromUrl download failed2: {0} url: {1} saveTo: {2}".FormatWith( err, config.url, saveToFilename );
            }
            return false;
        }


        private bool False( string msg )
        {
            StackFrame frame = new StackFrame( 1 );
            var method = frame.GetMethod();
            var type = method.DeclaringType;
            var name = method.Name;
            errorMsg = name + ": " + msg;
            return false;
        }

        private string CalcSubDir( string taskId )
        {
            return ( "000" + taskId.JustInt( "" ) ).Right( 3 );
        }

        private string dr_GetString( string fieldName )
        {
            if ( flds.ContainsKey( fieldName ) )
                //return fieldName + "=" + dataset[ fieldName ].ToString().LeftOf(" 12:00:00 AM");
                return dataset[ fieldName ].ToString().LeftOf( " 12:00:00 AM" );

            nof.Add( "nof: " + fieldName );
            return "nof";
        }

        private List<BpoPic> GenBpoPicList( MySqlDataReader drPics )
        {
            var picList = new List<BpoPic>();

            if ( drPics != null )
                while ( drPics.Read() )
                {
                    var pic = new BpoPic
                    {
                        fileName     = drPics[ "file_name" ].ToString(),
                        fileNameFnc  = drPics[ "file_name" ].ToString().Replace(".jpeg",".jpg"),
                        fileDir      = Path.Combine( config.bpoPicDir, CalcSubDir( drPics[ "task_id" ].ToString() ) ),
                        fileType     = drPics[ "file_type" ].ToString().ToStringDef( "jpg" ).Replace( ".", "" ),
                        dateTaken    = "",
                        description1 = drPics[ "description1" ].ToString(),
                        description2 = drPics[ "description2" ].ToString(),
                        description3 = drPics[ "description3" ].ToString(),
                        slot         = drPics[ "slot" ].ToString().ToLower()
                    };
                    if ( File.Exists( Path.Combine( pic.fileDir, pic.fileName ) ) )
                    {
                        pic.dateTaken = GetDateTakenFromImage( Path.Combine( pic.fileDir, pic.fileName )  ).ToString();
                        picList.Add( pic );
                    }                    
                }

            picCount = picList.Count();

            return picList;
        }

        private bool DownloadFiles( int runTid, SendResults results )
        {
            if ( results.response == null ) return False( "No Response from site" );

            var confirm = results.response.CONFIRMATION;

            if ( confirm == null ) return False( "No Confirmation from site" );
            if ( confirm.SUCCESS == null || confirm.SUCCESS.IsFalse() )
            {
                return False( "Confirmation.Reason: " + results.response.CONFIRMATION.REASON );
            }

            string urlPdf = null;
            string urlEnv = null;

            if ( results.isSubmitOk
              && results.response != null
              && results.response.URL != null
              && results.response.URL.Count() > 0 )
            {
                foreach ( var item in results.response.URL )
                {
                    if      ( item.TYPE.ToLower() == "pdf" ) urlPdf = item.Value;
                    else if ( item.TYPE.ToLower() == "env" ) urlEnv = item.Value;
                }
            }

            if ( urlPdf.HasValue() && urlEnv.HasValue() )
            {
                //get file pdf
                string getPdfErr;
                string filePdf = Path.Combine( config.savePdfDir, pid + "_" + runTid + ".pdf" );

                if ( ! GetFile( urlPdf, filePdf, out getPdfErr ) )
                {
                    return False( getPdfErr );
                }
                if ( ! File.Exists( filePdf ) )
                {
                    return False( "download is missing file {0}".FormatWith( filePdf ) );
                }

                if ( pdfOnly ) return true;

                //get file env
                string fileEnv = Path.Combine( config.saveEnvDir, pid + "_" + runTid + ".env" );

                if ( ! GetFile( urlEnv, fileEnv, out getPdfErr ) )
                {
                    return False( getPdfErr );
                }

                if ( ! File.Exists( fileEnv ))
                {
                    return False( "download is missing file {0}".FormatWith( fileEnv ) );
                }

                filePdfPath = filePdf;
                fileEnvPath = fileEnv;

                return true;
            }
            else 
            {
                return False( "download is missing file {0}".FormatWith( urlEnv.IsStrEmpty() ? "ENV" : "PDF" ) );
            }
        }

        //http: //stackoverflow.com/questions/180030/how-can-i-find-out-when-a-picture-was-actually-taken-in-c-sharp-running-on-vista
        //init this once so that if the function is repeatedly called it isn't stressing the garbage man

        private static Regex r = new Regex( ":" );

        //retrieves the datetime WITHOUT loading the whole image
        public static string GetDateTakenFromImage( string path )
        {
            using ( FileStream fs = new FileStream( path, FileMode.Open, FileAccess.Read ) )
            using ( Image myImage = Image.FromStream( fs, false, false ) )
            {
                try
                {
                    PropertyItem propItem = myImage.GetPropertyItem( 36867 );
                    string dateTaken = r.Replace( Encoding.UTF8.GetString( propItem.Value ), "-", 2 );
                    return DateTime.Parse( dateTaken ).ToString();
                }
                catch
                {
                    return "";
                }
            }
        }

    }
}
