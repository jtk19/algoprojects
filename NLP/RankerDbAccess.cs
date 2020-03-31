using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Artemis.Core.Model;


namespace ArtemisRank
{
    
    public class RankerDbAccess
    {
        /*--------------------------------------------------------------------------
         * Functions for the CITATIONS Table.
         * Table Format:
         *     key: ArticleGuid or other unique string ID
         *     value: "ArticleCreateTimestamp|citation1|citation2|..."
         */

        // This is the function the Crawler will call every time it adds a new Article
        // to the database, when the Crawler adds it, sumultaneously populating the
        // CITATONS table for that Article.
        public static void WriteArticleRankingDataToDb( RankingType_T rankingType,
                                                    Grouping verticalId,   
                                                    string articleId,   // Uniquely identifies the Article within the Vertical
                                                    DateTimeOffset articleCreationTime,
                                                    string articleUrl,
                                                    string articleText)
        {
            string key = articleId;
            string value = "";

            if (rankingType == RankingType_T.Popularity)
            {
                value = RankingDataProcessor.GetCitationsDbString(articleCreationTime,
                                                                     articleUrl,
                                                                     articleText);
            }
            else
            {
                StringBuilder sb = new StringBuilder( articleCreationTime.ToString() );
                sb.Append(RankingDataProcessor.Separator).Append(articleUrl);
                sb.Append(RankingDataProcessor.Separator).Append(articleText);
                value = sb.ToString();
            }

            if (!string.IsNullOrEmpty(value))
            {

                // Write <key, value> to database CITATIONS table for Vertical

            }
        }



        // The Ranker will call this functions to read the relevant citations to populate
        // the Citations Model and do the ranking.
        public static Dictionary<string, string> ReadRankingDataFromDb( RankingType_T rankingType,
                                        Grouping verticalId, 
                                        DateTimeOffset fromTime, 
                                        DateTimeOffset toTime )
        {
            Dictionary<string, string> citations = new Dictionary<string, string>();

            // Extract data from the citations table durion period: fromTime - toTime
            // store them as (<Article ID string>, <DB read value string>) pair in the Dictionary
            // for this Vertical.

            return citations;
        }



        // We do not need to keep citations past the time period we are considering (e.g. 36 hours)
        // In order to keep the database table access as effidient as possible, delete
        // records of older articles past the hours we need.
        public static void PruneCitationsTable(Grouping verticalId)
        {
            // keep this many extra hours of data in table
            uint keepDataHours = Ranker.RankingHoursConsidered + Ranker.RankingHoursBuffer +1;  

            DateTimeOffset limit = DateTimeOffset.Now.AddHours( -1 * keepDataHours );

            // Delete records in CITATIONS table older than time 'limit' for this vertical

        }


        /*--------------------------------------------------------------------------
         * Functions for the RANKING Table.
         * Table Format:
         *     key: ArticleGuid or unique string ID if the title Artcicle
         *     
         *     value: A string:
         *       If it is pure Popularity Ranking: "R|< pure popularity ranking score in range [0.00 , 1.00]"
         *       If it is Popularity and Similarity Ranking:
         *          "S|<Popularity score of title Article>|
         *             <ID of first similar Article>|<Similarity Score of first similar Article>|
         *             <ID of second similar Article>|<Similarity Score of second similar Article>|
         *             ...
         *             <ID of Nth similar Article>|<Similarity Score of Nth similar Article>"
         *            
         * After ranking ever N minutes, the Ranker will write the rankings to this table.
         * 
         * The front end will read the top ranked Articles from this table.
         */

        public static void WritePopularityRankedArticlesToDB( Grouping verticalId,
                                            Dictionary<string, double> popolarityScores,
                                            CancellationTokenSource cancellationToken )
        {
            ParallelOptions prlOpt = new ParallelOptions()
            {
                MaxDegreeOfParallelism = System.Environment.ProcessorCount,
                CancellationToken = cancellationToken.Token
            };

            var rankedList = 
                popolarityScores.OrderByDescending(y => y.Value);//.ToDictionary(y => y.Key, y => y.Value);

            StringBuilder sb;
            string key, value;

            // lock RANKING table so that no one else can access the table
            // clear RANKING table for Vertical
            foreach ( KeyValuePair< string, double> p in rankedList )
            {
                prlOpt.CancellationToken.ThrowIfCancellationRequested();

                if ( p.Value < 0.00000001 )
                {
                    continue; // skip, score too low
                }
                else
                {
                    key = p.Key;

                    sb = new StringBuilder("R");
                    sb.Append(RankingDataProcessor.Separator).Append(p.Value.ToString("F10"));
                    value = sb.ToString();

                    // write [ key, value ] to database RANKING table for Vertical
                    
                }
            }
            // unlock RANKING table
        }


        // Used by the front end to read the ranked Articles & scores
        // ordered in the descending order of the scores.
        // The front end can then use the top scored Articles' Guids/string IDs
        // to retrieve the popular Articles from the DocDB and display.
        public static SortedDictionary<double, string> ReadPopularityRankedArticlesFromDB( Grouping verticalId,
                                            CancellationTokenSource cancellationToken )
        {
            ParallelOptions prlOpt = new ParallelOptions()
            {
                MaxDegreeOfParallelism = System.Environment.ProcessorCount,
                CancellationToken = cancellationToken.Token
            };

            Dictionary<string, string> tmp = new Dictionary<string,string>();
            SortedDictionary<double, string> rankings = new SortedDictionary<double, string>(new DescDuplicateDoubleComp());
            double key;
            char[] sep = { RankingDataProcessor.Separator };

            // lock RANKING table
            // read the Ranked Articles from the RANKING table into tmp
            // unlock RANKING table

            foreach ( KeyValuePair<string, string> p in tmp )
            {
                prlOpt.CancellationToken.ThrowIfCancellationRequested();

                string[] parts = p.Value.Split(sep);
                key = Double.Parse(parts[1]);
                rankings[key] = p.Key;
            }

            //return tmp.OrderByDescending(y => y.Key).ToDictionary(y => y.Key, y => y.Value);
            return rankings;
        }

        
        public static void WritePopularitySimilarityRankedClustersToDb( 
                                    Grouping verticalId,
                                    SortedDictionary<double, PopularGroup> result,
                                    CancellationTokenSource cancellationToken )
        {
            ParallelOptions prlOpt = new ParallelOptions()
            {
                MaxDegreeOfParallelism = System.Environment.ProcessorCount,
                CancellationToken = cancellationToken.Token
            };

            // lock RANKING table so that no one else can access the table
            // clear RANKING table for this Vertical
            foreach ( KeyValuePair<double, PopularGroup> p in result )
            {
                prlOpt.CancellationToken.ThrowIfCancellationRequested();

                StringBuilder sb = new StringBuilder("S").Append(RankingDataProcessor.Separator)
                    .Append(p.Key.ToString("F10"));

                foreach ( KeyValuePair<double,string> q in p.Value.SimilarArticles )
                {
                    sb.Append(RankingDataProcessor.Separator).Append(q.Value)
                      .Append(RankingDataProcessor.Separator).Append(p.Key.ToString("F10"));
                }

                string key = p.Value.TitleArticle;
                string value = sb.ToString();

                // write [ key, value ] to the database RANKING table
            }
            // Unlock RANKING table 
        }


        public static SortedDictionary<DateTimeOffset, PopularGroup> ReadPopularitySimilarityRankedArticlesFromDb(
                                                Grouping verticalId,
                                                CancellationTokenSource cancellationToken )
        {
            ParallelOptions prlOpt = new ParallelOptions()
            {
                MaxDegreeOfParallelism = System.Environment.ProcessorCount,
                CancellationToken = cancellationToken.Token
            };

            SortedDictionary<DateTimeOffset, PopularGroup> results =
                    new SortedDictionary<DateTimeOffset, PopularGroup>(new DescDuplicateDateTimeOffsetComp());

            SortedDictionary<string, string> dataFromDb = new SortedDictionary<string, string>();
            // Lock RANKINGS table
            // Read (string, string> key,value pairs from the database into dataFromDb
            //unlock RANKINGS table

            char[] sep = { RankingDataProcessor.Separator };

            foreach ( string art in dataFromDb.Keys )
            {
                prlOpt.CancellationToken.ThrowIfCancellationRequested();

                string[] parts = dataFromDb[art].Split(sep);

                if ( parts[0] != "S" )
                {
                    throw new Exception("[ERROR: RankerDbAccess::ReadPopularitySimilarityRankedArticlesFromDb()]"
                                        + " Error in the data read in.");
                }

                DateTimeOffset key = DateTimeOffset.Parse(parts[1]);
                PopularGroup pop = new PopularGroup();
                pop.TitleArticle = art;
                
                for ( int i = 2; i < parts.Count(); i = i + 2 )
                {
                    double d = Double.Parse(parts[i + 1]);
                    pop.SimilarArticles[d] = parts[i];
                }

                results[key] = pop;   
            }

            return results;
        }

    }

}
