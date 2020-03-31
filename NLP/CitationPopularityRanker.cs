using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Configuration;

using log4net;
using Utilities;
using Model;
using Repository;


namespace Logic
{
    /// <summary>
    /// Metrics, mainly for those Articles discovered in citations that are not in the Pickscomb repo.
    /// </summary>
    public class RankingMetrics
    {
        public uint Ranking { get; set; }
        public double Score { get; set; }

        public RankingMetrics(uint r, double s)
        {
            Ranking = r;
            Score = s;
        }
    }

    /// <summary>
    /// Comparer that sorts in the descending order and which allows duplicate keys.
    /// </summary>
    public class DescDoubleComp : IComparer<double>
    {
        public int Compare( double x, double y)
        {
            int rtn;

            // use the default comparer to do the original comparison for doubles
            int ascendingResult = Comparer<double>.Default.Compare(x, y);

            if (ascendingResult == 0)  // allow duplicate keys by treating "equal" as "less than"
            {
                rtn = -1;
            }
            else  // turn the result around
            {
                rtn = 0 - ascendingResult;
            }

            return rtn;
        }
    }

 
    /// <summary>
    /// This class implements the Machine Learning algorithm of ranking according to the 
    /// Citation Based Time Weighted Popularity Model. Excutes the algorithm of the Model
    /// on the Articles in the Pickscomb DB.
    /// </summary>
    public class CitationPopularityRanker
    {
        //public static CitationPopularityRanker Instance = new CitationPopularityRanker();

        public uint NToIndex { get; set; }
        public uint NPreFetched { get; set; }         // The N for the top N popolar Articles to be pre-fetched during ranking
                                                    // Set to uint.Maxvalue to pre-fetch all Articles ranked from Pickscomb DB.       

 //       private SortedList<double, Article>[] _topNRankedArticles;      // The top N ranked Artcles already in the database, per each Vertical
        private SortedList<double, Guid>[] _allRankedArticleMetrics;    // For all Articls already in the database, per each Vertical
        private Dictionary<string, RankingMetrics>[] _rankedURLNotInDB; // Popular cited URLs with Articles not yet in DB
        private double[] _averagePopularityScore;                       // Average Score for each vrtical
        private bool[] dataSetV;                                        // Whether stats have been calculated for which verticals.
        private ILog logger;
        

        public CitationPopularityRanker()
        {
            NToIndex = 20;
            NPreFetched = 20;     // some default value
            int numVerticals = VerticalConstants.AllVerticals.Length;
  //          _topNRankedArticles = new SortedList<double, Article>[numVerticals];
            _allRankedArticleMetrics = new SortedList<double, Guid>[numVerticals];
            _rankedURLNotInDB = new Dictionary<string, RankingMetrics>[numVerticals];
            _averagePopularityScore = new double[numVerticals];

            dataSetV = new bool[numVerticals];

            for (uint i = 0; i < numVerticals; ++i)
            {
//                _topNRankedArticles[i] = new SortedList<double, Article>( new DescDoubleComp() );
                _allRankedArticleMetrics[i] = new SortedList<double, Guid>( new DescDoubleComp() );
                _rankedURLNotInDB[i] = new Dictionary<string, RankingMetrics>();
                _averagePopularityScore[i] = 0.0;
                dataSetV[i] = false;
            }
            logger = Utilities.Logger.GetLogger(typeof(CitationPopularityRanker));


            CitationStore store = null;
            try
            {
                store = CitationStore.Instance; // Causes the initial model to build from the repo statically.
            }
            catch ( Exception ex)
            {
                string msg = String.Format( "Instantiating CitationStore failed. Error: {0}", ex.Message );
                Logger.GetLogger(typeof(CitationPopularityRanker)).Error(msg);
                throw ex;
            }
        }


        // Reclaim the memory.
        public void clearAll()
        {
            for (uint i = 0; i < VerticalConstants.AllVerticals.Length; ++i)
            {
 //               _topNRankedArticles[i].Clear();
                _allRankedArticleMetrics[i].Clear();
                _rankedURLNotInDB[i].Clear();
                _averagePopularityScore[i] = 0.0;
                dataSetV[i] = false;
            }
        }

        // Reclaim memory for a particular Vertical.
        public void clearVertical( Guid vertical )
        {
            uint vid = VerticalConstants.getVerticalIndex(vertical);
//            _topNRankedArticles[vid].Clear();
            _allRankedArticleMetrics[vid].Clear();
            _rankedURLNotInDB[vid].Clear();
            _averagePopularityScore[vid] = 0.0;
            dataSetV[vid] = false;
        }

        public double getAveragePopularityScore( Guid vertical )
        {
            if (!dataSetV[VerticalConstants.getVerticalIndex(vertical)])
            {
                rankOnCitationPopularity(vertical);
            }
            return _averagePopularityScore[VerticalConstants.getVerticalIndex(vertical)];
        }

        /* Don't fetch from DB here. Operatoion too time expensive
        // Top N ranking Articles pre-fetched for you 
        public SortedList<double, Article> getTopRankedNArticles( Guid vertical )
        {
            if (!dataSetV[VerticalConstants.getVerticalIndex(vertical)])
            {
                rankOnCitationPopularity(vertical);
            }
            return _topNRankedArticles[VerticalConstants.getVerticalIndex(vertical)];
        }
         * */

        public SortedList<double, Guid> getAllRankedArticleMetrics(Guid vertical)
        {
            if (!dataSetV[VerticalConstants.getVerticalIndex(vertical)])
            {
                rankOnCitationPopularity(vertical);
            }
            return _allRankedArticleMetrics[VerticalConstants.getVerticalIndex(vertical)];
        }

        public Dictionary<string, RankingMetrics> getAllRankedURLNotInDB(Guid vertical)
        {
            if (!dataSetV[VerticalConstants.getVerticalIndex(vertical)])
            {
                rankOnCitationPopularity(vertical);
            }
            return _rankedURLNotInDB[VerticalConstants.getVerticalIndex(vertical)];
        }

      /*  public void reRank()
        {
            dataSetV[vIndex] = false;
            rankOnCitationPopularity();
        }
       * */

        public void reRank(Guid vertical)
        {
            uint vindex = VerticalConstants.getVerticalIndex(vertical);
            dataSetV[vindex] = false;
            rankOnCitationPopularity( vertical );
        }

        public void reRank(Guid vertical, CancellationTokenSource cancellationToken)
        {
            uint vindex = VerticalConstants.getVerticalIndex(vertical);
            dataSetV[vindex] = false;
            rankOnCitationPopularity(vertical, cancellationToken);
        }

        // Rank for all Verticals. 
        public  void rankOnCitationPopularity( )
        {
            for (uint i = 0; i < VerticalConstants.AllVerticals.Length; ++i)
            {
                rankOnCitationPopularity(i, NToIndex);
                dataSetV[i] = true;
            }
        }

        // Rank for a particular vertical only. 
        public void rankOnCitationPopularity( Guid vertical )
        {
            uint vIndex = VerticalConstants.getVerticalIndex(vertical);
            rankOnCitationPopularity( vIndex, NToIndex);
            dataSetV[vIndex] = true;
        }

        public void rankOnCitationPopularity( Guid vertical, CancellationTokenSource cancellationToken )
        {
            uint vIndex = VerticalConstants.getVerticalIndex(vertical);
            rankOnCitationPopularity( vIndex, NToIndex, cancellationToken);
            dataSetV[vIndex] = true;
        }

        public void rankOnCitationPopularity( uint vIndex , uint numRankedArticlesToExtract,
                                                CancellationTokenSource cancellationToken )
        {
            NPreFetched = 0;    // fetch none. //numRankedArticlesToExtract;
            dataSetV[vIndex] = false;

            Guid vid = VerticalConstants.AllVerticals[vIndex];

            var parallelOption = new ParallelOptions() 
            { 
                MaxDegreeOfParallelism = System.Environment.ProcessorCount, 
                CancellationToken = cancellationToken.Token 
            };


            IPickscombRepository repo = PickscombRepository.Instance;
            /*  // Do not fetch all articles from repo. Too expensive.
            uint numHours = CitationStore.Instance.CStore[vid].MaxHoursConsidered;
            DateTime start = DateTime.Now.AddHours(-1 * numHours);
            List<Article> articles = repo.GetArticles(vid, start, start.AddHours(numHours)).ToList<Article>();
             * */

            Guid artid;
            List<Guid> articlesWithCitations = new List<Guid>();

            CitationStore.Instance.CStore[vid].setTimeWeightedCitationPopularity();
            Dictionary<string, double> rankedURLs = CitationStore.Instance.CStore[vid].getTimeWeightedCitationPopularityScores();
            // make sure the URLs are sorted descending according to ranking score
            rankedURLs.OrderByDescending(y => y.Value).ToDictionary(y => y.Key, y => y.Value);

            uint ranking = 0, inDBCount = 0;
            foreach ( KeyValuePair<string,double> url in rankedURLs )
            {
                parallelOption.CancellationToken.ThrowIfCancellationRequested();

                ++ranking;
                
                if ( CitationStore.RepoArticleUrlGuidIndex[vid].ContainsKey(url.Key ) )
                {
                    // Article for this URL is in DB 
                    ++inDBCount;
                    artid = CitationStore.RepoArticleUrlGuidIndex[vid][url.Key];
                    articlesWithCitations.Add(artid);
                    _allRankedArticleMetrics[vIndex].Add(url.Value, artid);

                    // Log only the top 100
                    if (inDBCount < 100)
                    {
                        logger.DebugFormat(" Found Article Scored [{0}] in DB with Guid [{1}] Ranking [{2}]", 
                                                url.Value, artid, inDBCount);
                    }

                    /*
                    // Fetch and update in repo scores of only those Articles to be indexed; others scored at 0.0.
                    if (++inDBCount <= numRankedArticlesToExtract * 2)
                    {
                        Article art = repo.GetArticle(vid, artid);
                        art.Score = url.Value;
                        art.PopularityScore = url.Value;
                        art.PopularityRanking = ranking;
                        
                        logger.DebugFormat("[#{0}] Updated DB Article Score to [{1:F20}] for Giod [{2}].", inDBCount, art.Score, artid);
                    }
                    */

                  } 
                else //( !articleInDB )
                {
                    if (!_rankedURLNotInDB[vIndex].ContainsKey(url.Key) )
                    {
                        _rankedURLNotInDB[vIndex].Add(url.Key, new RankingMetrics(ranking, url.Value));
                    }
                    
                }
            }

            logger.Debug("\n");
            logger.DebugFormat("~~~ Found [{0}] Cited Articles in DB.", inDBCount);
            logger.DebugFormat("~~~ Found [{0}] Total Cited Article URLs.", ranking);
            if (ranking > 0)
            {
                logger.DebugFormat("~~~ [{0}] ({1:F4}%) Citated URL's do not have Articles in the database.\n\n",
                    ranking - inDBCount, (ranking - inDBCount) * 100 / ranking);
            }

            /* Don't fetch Articles fro DB here.  Fetch operation too time expensive.
            // Now add the Articles in the DB within the time frame but with no citations, to the
            // bottom if the list with a lower score than the lowest citation score.           
            double noCitationScore = lowestScore / 10;
            ++ranking;
            foreach (Article art in articles)
            {
                parallelOption.CancellationToken.ThrowIfCancellationRequested();

                if ( !articlesWithCitations.Contains( art.GetArticleId() ) )
                {
                    // Add this article with a score lower than the lowest score.
                    // They all have the same ranking and the same lowest score at the bottom.
                    art.PopularityRanking = ranking;        
                    art.PopularityScore = noCitationScore;
                    repo.Save(art);

                    _allRankedArticleMetrics[vIndex].Add(art.PopularityScore, art.GetArticleId());
                    if (++inDBCount <= NPreFetched)
                    {
                        _topNRankedArticles[vIndex].Add(art.PopularityScore, art);
                    }
                }
            }
             * */

            //_averagePopularityScore[vIndex] = CitationStore.Instance.CStore[vid].AveragePopularityScore;
            dataSetV[vIndex] = true;      
        }



        public void rankOnCitationPopularity( uint vIndex , uint numRankedArticlesToExtract )
        {
            NPreFetched =  0;
            dataSetV[vIndex] = false;

            Guid vid = VerticalConstants.AllVerticals[vIndex];

            IPickscombRepository repo = PickscombRepository.Instance;

            /* // Do not fetch all articles from repo; too expensive.
            // Fetch from database Articles going back MaxHoursConsidered ; default MaxHoursConsidered = 5 days * 24
            uint numHours = CitationStore.Instance.CStore[vid].MaxHoursConsidered;
            DateTime start = DateTime.Now.AddHours(-1 * numHours);
            List<Article> articles = repo.GetArticles( vid, start, start.AddHours(numHours)).ToList<Article>();
             * */

            Guid artid;
            List<Guid> articlesWithCitations = new List<Guid>();
            uint ranking = 0, inDBCount = 0;

            CitationStore.Instance.CStore[vid].setTimeWeightedCitationPopularity();
            Dictionary<string, double> rankedURLs = CitationStore.Instance.CStore[vid].getTimeWeightedCitationPopularityScores();
            // make sure the URLs are sorted descending according to ranking score
            rankedURLs.OrderByDescending(y => y.Value).ToDictionary(y => y.Key, y => y.Value);

            foreach ( KeyValuePair<string,double> url in rankedURLs )
            {
                ++ranking;
                
                if ( CitationStore.RepoArticleUrlGuidIndex[vid].ContainsKey(url.Key ) )
                {
                    // Article for this URL is in DB; map to Guid
                    ++inDBCount;
                    artid = CitationStore.RepoArticleUrlGuidIndex[vid][url.Key];
                    articlesWithCitations.Add(artid);

                    _allRankedArticleMetrics[vIndex].Add(url.Value, artid);

                    // Log only the top 100
                    if (inDBCount < 100)
                    {
                        logger.DebugFormat(" Found Article Scored [{0}] in DB with Guid [{1}] Ranking [{2}]",
                                                url.Value, artid, inDBCount);
                    }

                    /*
                    // Fetch and update in repo scores of only those Articles to be indexed; others scored at 0.0.
                    if (inDBCount <= numRankedArticlesToExtract)
                    {
                        Article art = repo.GetArticle(vid, artid);
                        art.Score = url.Value;
                        art.PopularityScore = url.Value;
                        art.PopularityRanking = ranking;
                        repo.Save(art);
                        logger.DebugFormat("[#{0}] Updated DB Article Score to [{1:F20}].", inDBCount, art.Score);
                    }
                    */
                } 
                else //( !articleInDB )
                {
                    _rankedURLNotInDB[vIndex].Add(url.Key, new RankingMetrics(ranking, url.Value));
                }

            }

            logger.Debug("\n");
            logger.DebugFormat("~~~ Found [{0}] Cited Articles in DB.", inDBCount);
            logger.DebugFormat("~~~ Found [{0}] Total Cited Article URLs.", ranking);
            if (ranking > 0)
            {
                logger.DebugFormat("~~~ [{0}] ({1:F4}%) Citated URL's do not have Articles in the database.\n\n",
                    ranking - inDBCount, (ranking - inDBCount) * 100 / ranking);
            }

            /* Don't fetch from repo here. Fetch operation too expensive.
             * Now add the Articles in the DB within the time frame but with no citations, to the
             * bottom if the list with a lower score than the lowest citation score.           
            double noCitationScore = lowestScore / 10;
            ++ranking;
            foreach (Article art in articles)
            {
                if ( !articlesWithCitations.Contains( art.GetArticleId() ) )
                {
                    // Add this article with a score lower than the lowest score.
                    // They all have the same ranking and the same lowest score at the bottom.
                    art.PopularityRanking = ranking;        
                    art.PopularityScore = noCitationScore;
                    repo.Save(art);

                    _allRankedArticleMetrics[vIndex].Add(art.PopularityScore, art.GetArticleId());
                    if (++inDBCount <= NPreFetched)
                    {
                        _topNRankedArticles[vIndex].Add(art.PopularityScore, art);
                    }
                }
            }
             * */

            _averagePopularityScore[vIndex] = CitationStore.Instance.CStore[vid].AveragePopularityScore;
            dataSetV[vIndex] = true;      
        }

    }


    /* Replaced by the Time Weighted Citation Popularity Algorithm above 
     * which can be ranked by 15 - 120 minute blocks.
     * The old one left here for reference for now.
    /// <summary>
    ///Implements the documented algo for recency and popularity (Doc 1). Basically  it is based on number of
    ///url citations to the given article in an hour. More citations in last hour leads to more score. The hours
    ///are counted from now till 36 hours in the past.
    /// </summary>
    public class CitationPopularity
    {
        int numHours = 36;

        public Dictionary<Guid, Double> ScoreBasedOnCitations(Guid verticalId, List<Article> articles, double decayRate)
        {
            Dictionary<Guid, Double> scores = new Dictionary<Guid, double>();
            var settings = ConfigurationManager.AppSettings["AzureStorage"];
            var space = ConfigurationManager.AppSettings["Space"];
            PickscombRepository repo = new PickscombRepository(space, settings);

            DateTime start = DateTime.Now.AddHours(-1 * numHours); //36 hours

            List<Article> articlesInLastNDays = repo.GetArticles(verticalId, start, start.AddHours(numHours)).ToList<Article>();

            Dictionary<int, List<Article>> hourWiseArticlesInLastNDays = PartitionArticlesBasedOnHours(articles, start);

            foreach (int hour in hourWiseArticlesInLastNDays.Keys)
            {
                List<Article> list = hourWiseArticlesInLastNDays[hour];
                Dictionary<string, List<string>> invertedIndex = BuildInvertedIndex(list);
                int totalNumCitations = 0;
                foreach (string key in invertedIndex.Keys)
                {
                    totalNumCitations += invertedIndex[key].Count;
                }

                foreach (Article article in articles)
                {
                    Guid articleId = article.GetRowId();
                    if (!scores.ContainsKey(articleId))
                    {
                        scores[articleId] = 0;
                    }
                    double score = (invertedIndex[article.PermaLink].Count / totalNumCitations)
                                            * 1000 * Math.Pow(Math.E, -1 * decayRate * hour);
                    scores[articleId] += score;
                }
            }

            return scores;
        }

        /// <summary>
        /// partitions a set of articles into hour based partitions
        /// </summary>
        /// <param name="articles"></param>
        /// <param name="start"></param>
        /// <returns></returns>
        public Dictionary<int, List<Article>> PartitionArticlesBasedOnHours(List<Article> articles, DateTime start)
        {
            Dictionary<int, List<Article>> hourBasedPartition = new Dictionary<int, List<Article>>();
            DateTime end = start.AddHours(numHours);
            for (int index = 1; index <= numHours; index++)
            {
                hourBasedPartition[index] = new List<Article>();
            }
            foreach (Article article in articles)
            {
                TimeSpan span = end - article.CreateDateTime;
                int hour = (int)Math.Ceiling(span.TotalHours);
                hourBasedPartition[hour].Add(article);
            }
            return hourBasedPartition;
        }

        /// <summary>
        /// builds a  inverted index of related link to article's permalink
        /// </summary>
        /// <param name="articles"></param>
        /// <returns></returns>
        public Dictionary<string, List<string>> BuildInvertedIndex(List<Article> articles)
        {
            Dictionary<string, List<string>> invertedIndex = new Dictionary<string, List<string>>();

            foreach (Article article in articles)
            {
                List<string> links = article.GetCitedLinks();
                foreach (string link in links)
                {
                    if (!invertedIndex.ContainsKey(link))
                        invertedIndex[link] = new List<string>();
                    invertedIndex[link].Add(article.PermaLink);
                }
            }
            return invertedIndex;
        }
    }
     * */
}
