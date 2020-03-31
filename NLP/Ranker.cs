using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Artemis.Core.Logging;
using Artemis.Core.Model;


namespace ArtemisRank
{
    public class Ranker
    {
        public static readonly ILogger Log = LogProvider.GetLogger(typeof(Ranker));

        private ParallelOptions _PrlOpt;
        private CancellationTokenSource _cToken;

        /// <summary>
        /// The Ranker is run every this many minutes.
        /// </summary>
        public static uint RankerRunPeriod = RankerConfig.GetRankerRunPeriod();  // minutes

        /// <summary>
        /// Citations are considered going back this many hours from now.
        /// </summary>
        public static uint RankingHoursConsidered = 36; // hours

        /// <summary>
        /// Articles are considered going back this many hours past the CitationConsiderationPeriod.
        /// </summary>
        public static uint RankingHoursBuffer = 6; // hours

        /// <summary>
        /// The vertical this Ranker is running for.
        /// </summary>
        private Grouping _verticalId = Grouping.HipHop;
        public Grouping VerticalId
        {
            get { return _verticalId; }
            set { _verticalId = value; }
        }
      
        /// <summary>
        /// The type of ranking this instance does.
        /// </summary>
        private RankingType_T _rankingType = RankingType_T.Popularity;
        public RankingType_T RankingType
        {
            get { return _rankingType; }
            set { _rankingType = value; }
        }

        /// <summary>
        /// The timestamp of the last model update and ranking re-scoring.
        /// </summary>
        private DateTimeOffset _lastDbRetrievalTime = DateTimeOffset.MinValue;
        public DateTimeOffset LastDbRetrievalTime
        {
            get { return _lastDbRetrievalTime; }
        }


        // The constructors.
        Ranker( Grouping verticalId )
        {
            VerticalId = verticalId;
            RankingType = RankerConfig.GetRankingType(verticalId);
        }

        Ranker(Grouping verticalId, RankingType_T rankingType )
        {
            VerticalId = verticalId;
            RankingType = rankingType;
        }


        /// <summary>
        /// This is the Run() function, the sole entry point, to start the Ranker.
        /// NOTE: You do nto start the Ranker every 15 minutes.
        /// You start the Ranker only once, and it runs forever.
        /// You restart only if it crashes or if the server re-starts
        /// </summary>
        /// <param name="cancellationToken"></param>
        public void Run( CancellationTokenSource cancellationToken)
        {
            _cToken = cancellationToken;
            _PrlOpt = new ParallelOptions()
            {
                MaxDegreeOfParallelism = System.Environment.ProcessorCount,
                CancellationToken = cancellationToken.Token
            };

            Log.Info("---------------------------------------------------------------------\n"
                        + "Ranker started for vertical {0}.\n", VerticalId.ToString());

            CitationPopularityModel citationModel = CitationPopularityModel.Instance;
            Log.Info("Citation Model instantiated.");

            SimilarityRanker similarityModel = SimilarityRanker.Instance;
            if ( _rankingType == RankingType_T.PopularitySimilarity )
            {
                Log.Info("TF-IDF Similarity Ranker instantiated.");
            }


            DateTimeOffset toTime = DateTimeOffset.Now;
            uint readPeriod = Ranker.RankingHoursConsidered + Ranker.RankingHoursBuffer;
            DateTimeOffset fromTime = toTime.AddHours(-1 * readPeriod);

            // Get data from fromTime to Now from the CITATIONS table
            Dictionary<string, string> rankingData;
            try
            {
                rankingData = RankerDbAccess.ReadRankingDataFromDb(_rankingType, _verticalId,
                                                                fromTime, toTime);
            }
            catch (Exception ex)
            {
                Log.Error("Reading CITATIONS table from the database failed.");
                throw ex;
            }
            Log.Info("Ranking Data successfully read in from the CITATIONS table.");
            _PrlOpt.CancellationToken.ThrowIfCancellationRequested();


            try
            {
                citationModel.Init( VerticalId, RankingType, rankingData, _cToken );
            }
            catch (Exception ex)
            {
                Log.Error("[FATAL]  Citation Popularity Ranker initialization failed!");
                throw ex;
            }
            Log.Info("Citation Model built for the past {0} hours.\n",
                                Ranker.RankingHoursConsidered);
            _PrlOpt.CancellationToken.ThrowIfCancellationRequested();


            // The first ranking run.
            // Estimate the top ranking Articles and their popularity scores
            try
            {
                citationModel.SetTimeWeightedCitationPopularity();
            }
            catch (Exception ex)
            {
                Log.Error("Citation Popularity Calculation failed in re-rank. ");
                throw ex;
            }
            Log.Info("Time weighted Citation Popularity set.");
            _PrlOpt.CancellationToken.ThrowIfCancellationRequested();

            if ( _rankingType == RankingType_T.Popularity)
            {
                // Write top ranking Articles and Scores to the RANKING table
                citationModel.WriteRankedArticlesToDB();
                Log.Info("Wrote the latest Citation Popularity scores to the database.");
            }


            //-----------------------------------------------------------------------
            // Similarity Ranker initial run
            if ( _rankingType == RankingType_T.PopularitySimilarity)
            {
                try
                {
                    similarityModel.Init(VerticalId, rankingData, _cToken);
                }
                catch (Exception ex)
                {
                    Log.Error("[FATAL]  TF-IDF Similarity Ranker initialization failed!");
                    throw ex;
                }
                Log.Info("TF-IDF Similarity Model built for the past {0} hours.\n",
                                    Ranker.RankingHoursConsidered + Ranker.RankingHoursBuffer );
                _PrlOpt.CancellationToken.ThrowIfCancellationRequested();

                RunSimilarityModel( citationModel, similarityModel);
                _PrlOpt.CancellationToken.ThrowIfCancellationRequested();
            }

            Log.Info( "------- Ranker initialization and first run completed. -------\n\n");


            // The last time the data was retrieved and the models successfully built from it.
            _lastDbRetrievalTime = toTime;

            //----------------------------------------------------------------------
            // Ranker initialization completed.  Now run the Ranker infinite loop.
            TimeSpan processingTime, sleepTime, rankerInterval;
            rankerInterval = TimeSpan.FromMinutes(Ranker.RankerRunPeriod);

            while (true)
            {
                _PrlOpt.CancellationToken.ThrowIfCancellationRequested();

                processingTime = DateTimeOffset.Now - LastDbRetrievalTime;
                if (rankerInterval > processingTime)
                {
                    sleepTime = rankerInterval - processingTime;
                    Thread.Sleep(sleepTime);
                }

                _PrlOpt.CancellationToken.ThrowIfCancellationRequested();


                // Get last interval's data from the CITATIONS table
                toTime = DateTimeOffset.Now;
                fromTime = LastDbRetrievalTime;

                // get data from fromTime to Now from the CITATIONS table
                Log.Info("-------------------------------------------------------------------------------");
                Log.Info("New run. Retrieving Ranking data from the DB tables.");
                try
                {
                    rankingData = RankerDbAccess.ReadRankingDataFromDb( _rankingType, _verticalId, 
                                                                        fromTime, toTime);
                }
                catch (Exception ex)
                {
                    Log.Error("Reading CITATIONS table from the database failed.");
                    throw ex;
                }
                _lastDbRetrievalTime = toTime;
                Log.Info("Ranking data fetched from the database.");
                _PrlOpt.CancellationToken.ThrowIfCancellationRequested();

                // Update the Citation Model for the last period from the data retrieved
                try
                {
                    citationModel.LoadCitationsModel( RankingType, rankingData, toTime );
                }
                catch (Exception ex)
                {
                    Log.Error("Ranker Update: LoadCitationsModel() failed.");
                    throw ex;
                }
                Log.Info("Citation Model successfully re-loaded since the last run.");
                _PrlOpt.CancellationToken.ThrowIfCancellationRequested();


                // Estimate the top ranking Articles and their popularity scores
                try
                {
                    citationModel.SetTimeWeightedCitationPopularity();
                }
                catch (Exception ex)
                {
                    Log.Error("Citation Popularity Calculation failed in re-rank. ");
                    throw ex;
                }
                Log.Info("Time weighted Citation Popularity set.");
                _PrlOpt.CancellationToken.ThrowIfCancellationRequested();

                if (_rankingType == RankingType_T.Popularity)
                {
                    // Write top ranking Articles and Scores to the RANKING table
                    citationModel.WriteRankedArticlesToDB();
                    Log.Info("Wrote the latest Citation Popularity scores to the database.");
                }

                // The front end then reads the top ranked articles directly from the RANKING table
                // RANKING table:
                // Key: Article Guid/ unique string ID
                // Value: double RankingScore in range [0.00 , 1.00]>
                // 
                // The front end will then be able to retrieve the most popular articles from the
                // database using the above info and display on the front page.


                //---------------------------------------------------------------------------------
                // TF-IDF Similarity Ranking
                //
                
                if ( RankingType == RankingType_T.PopularitySimilarity )
                {
                    try
                    {
                        similarityModel.UpdateModel( VerticalId, 
                                                     rankingData, 
                                                     _cToken);
                    }
                    catch ( Exception )
                    {
                        Log.Error("[ERROR] TF-IDF Similarity Model initialization failed!");
                        goto CleanupCitations;
                    }
                    Log.Info("TF-IDF Similarity Model initialized successfully.");
                    _PrlOpt.CancellationToken.ThrowIfCancellationRequested();


                    RunSimilarityModel(citationModel, similarityModel);
                    _PrlOpt.CancellationToken.ThrowIfCancellationRequested();

                }

                
            CleanupCitations:         
                // cleanup and housekeeping
                citationModel.PruneCitationsIndex();
                RankerDbAccess.PruneCitationsTable( VerticalId );
                Log.Info("Housekeeping tasks completed.\n");

                Log.Info( "------- Ranker rerun completed. -------\n\n");
            }

        }


        private void RunSimilarityModel( CitationPopularityModel citationModel,
                                         SimilarityRanker similarityModel   )
        {
            Dictionary<string, double> scoredArticles = citationModel.PopularityScoredArticles;

            SortedDictionary<double, PopularGroup> result =
                    new SortedDictionary<double, PopularGroup>(new DescDuplicateDoubleComp());

            while (scoredArticles.Count() > similarityModel.MaxNumSimilar)
            {
                _PrlOpt.CancellationToken.ThrowIfCancellationRequested();

                KeyValuePair<string, double> thisArt = getMaxVal(scoredArticles);

                var similarArt = similarityModel.GetTopSimilarArticlesTo(thisArt.Key);

                PopularGroup pop = new PopularGroup();
                pop.TitleArticle = thisArt.Key;
                foreach (KeyValuePair<double, string> p in similarArt)
                {
                    _PrlOpt.CancellationToken.ThrowIfCancellationRequested();

                    try {  pop.SimilarArticles[p.Key] = p.Value; }
                    catch (Exception) { }

                    try { scoredArticles.Remove(p.Value); }
                    catch (Exception) { }
                }
                result[thisArt.Value] = pop;

                // remove already ranked articles
                try { scoredArticles.Remove(thisArt.Key); }
                catch (Exception) { }
            }

            // The few left overs
            foreach (string art in scoredArticles.Keys)
            {
                _PrlOpt.CancellationToken.ThrowIfCancellationRequested();

                PopularGroup pop = new PopularGroup();
                pop.TitleArticle = art;
                result[scoredArticles[art]] = pop;
            }

            // Write the fully ranked cluster results to the database
            RankerDbAccess.WritePopularitySimilarityRankedClustersToDb(
                                                    VerticalId, result, _cToken);

        }


        private KeyValuePair<string, double> getMaxVal( Dictionary<string, double> vec )
        {
            KeyValuePair<string, double> rtn = vec.ElementAt(0);

            foreach ( KeyValuePair<string, double> p in vec )
            {
                if ( p.Value > rtn.Value )
                {
                    rtn = p;
                }
            }
            return rtn;
        }
    }
}
