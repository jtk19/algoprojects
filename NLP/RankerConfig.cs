using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Configuration;
using Artemis.Core.Model;


namespace ArtemisRank
{
    public class RankerConfig
    {
        public static uint GetRankerRunPeriod()
        {
            uint period = 15;

            try
            {
                period = Convert.ToUInt32(ConfigurationManager.AppSettings["RankerRunPeriodMins"]);
            }
            catch ( Exception )
            {
                period = 15;
            }

            if ( period < 15  || period > 120 ) // outside limits, set to default
            {
                period = 15;
            }

            return period;
        }

        public static RankingType_T GetRankingType( Grouping vertical )
        {
            RankingType_T rtype = RankingType_T.Popularity;

            string key = null, value = null;
            switch ( vertical )
            {
                case Grouping.FashionMen: key = "FasionMenRankingType";
                                          break;
                case Grouping.FashionWomen: key = "FasionWomenRankingType";
                                          break;
                case Grouping.HipHop: key = "FasionTbsrRankingType";
                                          break;
                default: key = null; break;
            }

            if ( String.IsNullOrEmpty(key) )
            {
                return RankingType_T.Popularity;
            }

            try
            {
                value = ConfigurationManager.AppSettings["RankerRunPeriodMins"].ToLower();

                if ( value == "popularitysimilarity" )
                {
                    rtype = RankingType_T.PopularitySimilarity;
                }
                else
                {
                    rtype = RankingType_T.Popularity;
                }
            }
            catch ( Exception )
            {
                rtype = RankingType_T.Popularity;
            }

            return rtype;
        }
    }
}
