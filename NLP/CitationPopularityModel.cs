using Artemis.Core.Model;
using Artemis.Core.Logging;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System;
using System.Collections.Generic;
using System.Text;


namespace ArtemisRank
{

    public class CitationPopularityModel
    {

        public static CitationPopularityModel Instance = new CitationPopularityModel();


        private static readonly ILogger Log =  LogProvider.GetLogger(typeof(CitationPopularityModel));

        private ParallelOptions _PrlOpt;
        private CancellationTokenSource _cToken;

        /// <summary>
        /// Citations are counted per this period in minutes at a time going back and
        /// weighted on the weighting curve, the more recent periods' counts weighted higher.
        /// </summary>
        public static uint CitationWeightingPeriod = 15;  // minutes

        /// <summary>
        /// The Index of MatchingUrl to (Article ID, Article Createtime) map per this vertical 
        /// for all the Articles in the repository within the considered hours. 
        /// </summary>
        private static Dictionary<string, ArtcileIndexInfo> ArticleUrlIdIndex =
                                        new Dictionary<string, ArtcileIndexInfo>();


        /// <summary>
        /// The main Citation model: For each Article URL, a list of citation timestamps sorted by 
        /// timestamp ascending.  Since there can be more than 1 citation with the same timestamp, 
        /// the citation count is uint.
        /// </summary>
        private Dictionary<string, SortedDictionary<DateTimeOffset, uint>> CitationModel =
                                    new Dictionary<string,SortedDictionary<DateTimeOffset,uint>>();

        private Grouping _verticalId = Grouping.HipHop;

        //private Dictionary<string, double> _popularityScore  = new Dictionary<string,double>();
        private bool _popularitySet = false;

        private double _normalizer = 1.0;

        private Dictionary<string, double> _popularArticlesInDB =  new Dictionary<string, double>();
        public Dictionary<string, double> PopularityScoredArticles
        {
            get { return _popularArticlesInDB; }
        }

        private Dictionary<string, double> _citedArticlesNotInDB =
                                        new Dictionary<string, double>();



        /// <summary>
        /// Builds the initial Citation Model and the Index.
        /// </summary>
        public void Init( Grouping verticalId, 
                          RankingType_T rankingType,
                          Dictionary<string, string> rankingData,
                          CancellationTokenSource cancellationToken)
        {
            _verticalId = verticalId;

            _cToken = cancellationToken;
            _PrlOpt = new ParallelOptions()
            {
                MaxDegreeOfParallelism = System.Environment.ProcessorCount,
                CancellationToken = cancellationToken.Token
            };

            // Timeframe considered
            DateTimeOffset toTime = DateTimeOffset.Now;
            uint readPeriod = Ranker.RankingHoursConsidered + Ranker.RankingHoursBuffer;
            DateTimeOffset fromTime = toTime.AddHours( -1 * readPeriod);


            // Build the initial citations model
            try
            {
                LoadCitationsModel( rankingType, rankingData, toTime);
            }
            catch (Exception ex)
            {
                Log.Error("Ranker Init(): LoadCitationsModel() failed.");
                throw ex;
            }
            Log.Info("Initial Citation Model build completed.");


            try
            {
                SetTimeWeightedCitationPopularity();
            }
            catch (Exception ex)
            {
                Log.Error("Citation Popularity Calculation failed in Init(). ");
                throw ex;
            }

            Log.Info("Citation Polularity scores set.");
        }


        public void LoadCitationsModel( RankingType_T rankingType, 
                                        Dictionary<string, string> dbStr,
                                        DateTimeOffset to_time)
        {
            KeyValuePair<DateTimeOffset, List<string>> cit;
            DateTimeOffset modelTimeLimit = to_time.AddHours(-1 * Ranker.RankingHoursConsidered);
            string matchingUrl;

            foreach (KeyValuePair<string, string> rec in dbStr)
            {
                _PrlOpt.CancellationToken.ThrowIfCancellationRequested();

                cit = RankingDataProcessor.GetCitationsFromDbString(rankingType, rec.Value);

                // Add the title Article's URL to the Index
                matchingUrl = RankingDataProcessor.GetMatchingUrl(cit.Value[0]);

                ArtcileIndexInfo idxval = new ArtcileIndexInfo();
                idxval.ArticleId = rec.Key;
                idxval.ArtcileDatetime = cit.Key;

                try
                {
                    if ( !string.IsNullOrEmpty(matchingUrl) )
                        ArticleUrlIdIndex[matchingUrl] = idxval;
                }
                catch ( Exception )  // This Article is already in Index
                {
                    Log.Warn( "Ranker init(): Error adding Article for matching URL [{0}] to Index for Article: {1}",
                                matchingUrl,  
                                rec.Key.ToString() );
                }
                

                // Build the citation model.
                if (idxval.ArtcileDatetime >= modelTimeLimit)
                {
                    try
                    {
                        AddCitationsInArticle(cit.Key, cit.Value);
                    }
                    catch (Exception)
                    {
                        Log.Warn("Adding Citations in Article with ID [{0}] failed, URL: {1}",
                                       rec.Key.ToString(), rec.Value[0]);
                        //throw;
                    }
                }
            }

        }

        /// <summary>
        /// Add all the citations in the Article to the Citations Model.
        /// </summary>
        /// <param name="timestamp">timestamp of the parent Article</param>
        /// <param name="citations">the list of citations in the Article</param>
        private void AddCitationsInArticle( DateTimeOffset timestamp, List<string> citations )
        {
            // leave out for i=0; this is the parent Article's URL
            for (int i = 1; i < citations.Count; ++i)
            {
                _PrlOpt.CancellationToken.ThrowIfCancellationRequested();

                string matchingUrl = RankingDataProcessor.GetMatchingUrl(citations[i]);
                if (!string.IsNullOrEmpty(matchingUrl))
                {
                    try
                    {
                        AddCitation(matchingUrl, timestamp);
                    }
                    catch ( Exception ex)
                    {
                        Log.Warn( "AddCitation() failed at timestamp[{0}] for URL [{1}]",
                                    timestamp.ToString("yyyy/MM/dd HH:mm:ss"), citations[i] );
                    }
                }
            }
        }

        /// <summary>
        /// Add a single citation to the Citation Model.
        /// </summary>
        /// <param name="citation">citation MatchingURL</param>
        /// <param name="timestamp">timestamp of the citation</param>
        private void AddCitation( string citation, DateTimeOffset timestamp)
        {
            if (CitationModel.ContainsKey(citation)) // Model already contains citations for this URL
            {
                if ( CitationModel[citation].ContainsKey( timestamp ) )
                {
                    ++(CitationModel[citation][timestamp]);
                }
                else
                {
                    CitationModel[citation].Add( timestamp, 1 );
                }
            }
            else
            {
                SortedDictionary<DateTimeOffset, uint> lst = new SortedDictionary<DateTimeOffset, uint>();
                lst.Add(timestamp, 1);
                CitationModel.Add( citation, lst );
            }
        }


        public int SetTimeWeightedCitationPopularity()
        {
            _popularitySet = false;

            if ( CitationModel.Count == 0 )
            {
                Log.Error("[setTimeWeightedCitationPopularity()] CitationStore model empty for vertical {0}.", 
                                _verticalId.ToString() );
                return -1;
            }

            uint numPeriods = Ranker.RankingHoursConsidered * 60 / CitationPopularityModel.CitationWeightingPeriod;
            if ((Ranker.RankingHoursConsidered * 60) % CitationPopularityModel.CitationWeightingPeriod != 0)
            {
                ++numPeriods;
            }

            Dictionary<string, uint[]> cCountsPerURL = new Dictionary<string, uint[]>();   // per period citations counts for each URL
            uint [] totalCCounts = new uint[numPeriods];

            List<DateTimeOffset> removeList;  // for cleaning up citations past the max hours limit
            uint p;  // the period count
            DateTimeOffset periodEnd;

            foreach ( KeyValuePair<string, SortedDictionary<DateTimeOffset, uint>> i in CitationModel)
            {
                _PrlOpt.CancellationToken.ThrowIfCancellationRequested();

                cCountsPerURL[ i.Key ] = new uint[numPeriods];
                removeList = new List<DateTimeOffset>();

                periodEnd = DateTimeOffset.Now.AddMinutes(-1 * CitationPopularityModel.CitationWeightingPeriod);
                p = 0;

                foreach ( KeyValuePair<DateTimeOffset, uint> j in i.Value )
                {
                    _PrlOpt.CancellationToken.ThrowIfCancellationRequested();

                    if (j.Key < periodEnd)  // Advance to next period backwards in time
                    {
                        ++p;
                        if (p > numPeriods)  // Mark this citations as past the last max allowed hours.
                        {
                            removeList.Add(j.Key);
                            continue;
                        }
                       
                        periodEnd = periodEnd.AddMinutes(-1 * CitationPopularityModel.CitationWeightingPeriod);
                        
                    }

                    cCountsPerURL[i.Key][p] += j.Value;
                    totalCCounts[p] += j.Value;
                }

                // Now remove citations past the last Ranker.CitationHoursConsidered hours considered 
                // for this URL from the CitationModel.
                foreach (DateTimeOffset dt in removeList)
                {
                    i.Value.Remove(dt);
                }
                removeList.Clear();
            }

            // Cleanup continued: Remove any URLs with no citations within the allowed Max hours.
            foreach (string url in CitationModel.Keys)
            {
                _PrlOpt.CancellationToken.ThrowIfCancellationRequested();

                if (CitationModel[url].Count == 0)
                {
                    CitationModel.Remove(url);
                }
            }

            // Time Weighting
            RecencyWeightingCurve weightingCurve = new RecencyWeightingCurve();
            double[] timeWeighting = weightingCurve.GetRecencyCurve(numPeriods);

            /* Estimate the Time Weighted Citation Based Popularity of each article. */
            Dictionary<string, double> popularityScore = new Dictionary<string, double>();
            foreach (KeyValuePair<string, uint[]> i in cCountsPerURL)
            {
                _PrlOpt.CancellationToken.ThrowIfCancellationRequested();

                popularityScore[i.Key] = 0.0;
                for (uint j = 0; j < numPeriods; ++j)
                {
                    _PrlOpt.CancellationToken.ThrowIfCancellationRequested();

                    if (totalCCounts[j] > 0)
                    {
                        if (cCountsPerURL[i.Key][j] > 0)
                        {
                            popularityScore[i.Key] += 1000 * timeWeighting[j] * cCountsPerURL[i.Key][j] / totalCCounts[j];
                        }
                    }
                    else
                    {
                        if (cCountsPerURL[i.Key][j] > 0)
                        {
                            string msg = "[Ranker::SetTimeWeightedCitationPopularity()]"
                                + " Something went wrong for URL " + i.Key + " in time period " + j.ToString()
                                + "\n URL citation count " + cCountsPerURL[i.Key][j].ToString()
                                + " / total citation count " + totalCCounts[j].ToString();
                            Log.Error(msg);
                        }
                    }
                }
            }
            cCountsPerURL.Clear(); // reclaim space

            //_popularityScore.Clear();
            //_popularityScore = 
            //    popularityScore.OrderByDescending(y => y.Value).ToDictionary(y => y.Key, y => y.Value);


            // Map <matchingURL, RankingScore> in __popularityScore
            // to the Article Guid/stting ID and Article full URL using the Index.
            ArtcileIndexInfo idx;
            double maxScore = 0.0000000001;
            _popularArticlesInDB.Clear();
            _citedArticlesNotInDB.Clear();
            foreach (KeyValuePair<string, double> art in popularityScore)
            {
                _PrlOpt.CancellationToken.ThrowIfCancellationRequested();

                try
                {
                    idx = ArticleUrlIdIndex[art.Key];

                    _popularArticlesInDB.Add( idx.ArticleId , art.Value );

                    if (art.Value > maxScore)
                        maxScore = art.Value;
                }
                catch (KeyNotFoundException)
                {
                    _citedArticlesNotInDB.Add(art.Key, art.Value);
                }
            }

            // normalize to range [0.0 , 1.0]
            if (maxScore > 0.0000000001)
            {
                foreach (string i in _popularArticlesInDB.Keys)
                {
                    _PrlOpt.CancellationToken.ThrowIfCancellationRequested();

                    _popularArticlesInDB[i] = _popularArticlesInDB[i] / maxScore;
                }
                _normalizer = maxScore;
            }

            _popularitySet = true;

            return 0;
        }


        public void WriteRankedArticlesToDB()
        {
            if (!_popularitySet)
            {
                Log.Warn("WriteRankedArticlesToDB() is trying to write into RANKING without calculating model.");
                Log.Warn("Setting popularity first.");
                SetTimeWeightedCitationPopularity();
                if (!_popularitySet)
                {
                    Log.Error("WriteRankedArticlesToDB(): Setting popularity failed.");
                    Log.Error("Could not write popularity scores to the RANKING table.");
                    return;
                }
            }
            // Write top ranking Articles and Scores to RANKING table from _popolarArticlesInDB
            RankerDbAccess.WritePopularityRankedArticlesToDB( _verticalId, _popularArticlesInDB,
                                                                 _cToken );
        }


        public List<string> GetPopularArticleIds()
        {
            if (!_popularitySet)
            {
                Log.Warn("GetPopularArticles() is trying to write into RANKING without calculating model.");
                Log.Warn("Setting popularity first.");
                SetTimeWeightedCitationPopularity();
                if (!_popularitySet)
                {
                    Log.Error("GetPopularArticles(): Setting popularity failed.");
                    Log.Error("Could not write popularity scores to the RANKING table.");
                    return null;
                }
            }

            return _popularArticlesInDB.Keys.ToList();
        }


        /// <summary>
        /// Remove from the Index Article data past the time limit we are considering.
        /// </summary>
        public void PruneCitationsIndex()
        {
            uint timeLimitHours = Ranker.RankingHoursConsidered + Ranker.RankingHoursBuffer;
            DateTimeOffset timeLimit = DateTimeOffset.Now.AddHours(-1 * timeLimitHours);

            foreach (string i in ArticleUrlIdIndex.Keys)
            {
                if (ArticleUrlIdIndex[i].ArtcileDatetime < timeLimit)
                {
                    ArticleUrlIdIndex.Remove(i);
                }
            }
        }
                
    }  // end class Ranker


}
