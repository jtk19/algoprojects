using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections;
using System.Configuration;
using System.Text.RegularExpressions;

using log4net;
using Model;
using Utilities;
using Repository;


// TO DO: Retrieve Logger just once and store.


namespace Logic
{
    /// <summary>
    /// This class implements the Statistical Model of Citation Based Time Weighted Popularity,
    /// extending the model separately to all the verticals.
    /// </summary>
    public class CitationStore
    {
        // Maintains a single static store of Citations for all Verticals in one place.
        public static CitationStore Instance = new CitationStore();

        /// <summary>
        /// The Index of MatchingUrl to Guid map per each Vertical for all the Articles
        /// in the repository within the considered hours. (Default the last 60 hours.)
        /// </summary>
        public static Dictionary<Guid, Dictionary<string, Guid>> RepoArticleUrlGuidIndex;


        private Dictionary<Guid, VerticalCitationStore> _vertCStore;
        public Dictionary<Guid, VerticalCitationStore> CStore
        {
            get { return _vertCStore; }
        }

        private uint _maxHours = 72; // default 3 days 
        public uint MaxHoursConsidered
        {
            get { return _maxHours; }
            set
            {
                uint _maxHours = value;

                if (_maxHours < 36 || _maxHours > 30 * 24) // Out of allowed range [1.5 (36 hours), 30] days.
                {
                    _maxHours = 72;   // default 3 days
                }
                for (int i = 0; i < VerticalConstants.AllVerticals.Length; ++i)
                {
                    _vertCStore[VerticalConstants.AllVerticals[i]].MaxHoursConsidered = _maxHours;
                }
            }
        }

        private uint _period = 15; // defualt 15 minute intervals
        public uint Period
        {
            get { return _period; }
            set
            {
                _period = value;
                if (_period < 15 || _period > 120) // Out of allowed range.
                {
                    _period = 15;   // default 15 minutes
                }
                for (int i = 0; i < VerticalConstants.AllVerticals.Length; ++i)
                {
                    _vertCStore[VerticalConstants.AllVerticals[i]].Period = _period;
                }
            }
        }


        public CitationStore()
        {
            CitationStore.RepoArticleUrlGuidIndex = new Dictionary<Guid, Dictionary<string, Guid>>();
            _vertCStore = new Dictionary<Guid, VerticalCitationStore>();
            foreach (Guid verticalId in VerticalConstants.AllVerticals)
            {
                CitationStore.RepoArticleUrlGuidIndex[verticalId] = new Dictionary<string, Guid>();
                _vertCStore[verticalId] = new VerticalCitationStore( verticalId );
            }

        }

        public void AddCitationsInArticle(Article article)
        {
            try
            {
                _vertCStore[article.GetVerticalId()].AddCitationsInArticle(article);
            }
            catch (Exception)
            {
                string msg = "[CitationStore.AddCitationsInArticle()] ERROR: "
                    + " There is no citationsStore for Vertical ID : " + article.GetVerticalId();
                Logger.GetLogger(typeof(CitationStore)).Error(msg);
            }
        }

    }


    /// <summary>
    /// This class implements the Statistical Model of Citation Based Time Weighted Popularity
    /// for one particular vertical.
    /// </summary>
    public class VerticalCitationStore
    {

        /* Citation store: for each Article URL, a list of citation timestamps sorted by timestamp ascending.
         * Since there can be more than 1 citation with the same timestamp, the citation count is uint. */
        private Dictionary<string, SortedList<DateTimeOffset, uint>> _cstore;

        private Dictionary<string, double> _popularityScore; // Popularity Score for each Article URL with citations in the store
        private string[] _popularityRankedURLs;               // Article URLs sorted descending according to popularity.
        private bool _popularitySet = false;

        private static ILog logger = Utilities.Logger.GetLogger(typeof(VerticalCitationStore));

        private double _averageScore = 0.0;
        public double AveragePopularityScore
        {
            get
            {
                if (!_popularitySet)
                {
                    setTimeWeightedCitationPopularity(_period);
                }
                return _averageScore;
            }
        }

        public int StoreCount
        {
            get { return _cstore.Count; }
        }

        // Calculate citation popularity per every Period minutes
        private uint _period = 15;
        public uint Period
        {
            get { return _period; }
            set
            {
                _period = value;
                if (_period < 15 || _period > 120) // Out of allowed range.
                {
                    _period = 15;   // default 15 minutes
                }

            }
        }

        // Citations over this length are stored and considered for the ranking; 
        // heavily discounted after 36 hours.
        private uint _maxHours = 3 * 24;     // default 5 days
        public uint MaxHoursConsidered
        {
            get { return _maxHours; }
            set
            {
                _maxHours = value;
                if (_maxHours < 36 || _maxHours > 30 * 24) // Out of allowed range [1.5, 30] days.
                {
                    _maxHours = 72;   // default 72 hours
                }

            }
        }

        public Guid VerticalId { get; set; }


        public VerticalCitationStore( Guid verticalId )
        {
            VerticalId = verticalId;

            try
            {
                Period = Convert.ToUInt32(ConfigurationManager.AppSettings["RankingInterval"]);

            }
            catch (Exception)
            {
                Period = 15; // Default, run every 15 minutes
            }
            MaxHoursConsidered = 72;

            _cstore = new Dictionary<string, SortedList<DateTimeOffset, uint>>();
            _popularityScore = new Dictionary<string, double>();

            _popularitySet = false;

            initModelFromRepo();
        }


        private void initModelFromRepo()
        {
            string msg;

            DateTime endt = DateTime.UtcNow;
            DateTime startt = DateTime.UtcNow.AddHours(-1 * MaxHoursConsidered);
            //DateTime startt = DateTime.UtcNow.AddHours(-1 * 36);    // for testing

            msg = String.Format("initModelFromRepo(): Building Initial Model for Articles in Vertical {0} \n\t from startTime [{1}] to endTime [{2}].",
                                 VerticalConstants.GetFriendlyName(VerticalId), startt.ToString(), endt.ToString());
            logger.Info(msg);

            logger.Debug("Retrieving Articles from Pickscomb repository.");
            List<Article> articles = null;
            try
            {
                IPickscombRepository repo = PickscombRepository.Instance;
                logger.Info("PickscombRepository instantiated. Fetching Articles . . .");
                articles = repo.GetArticles(VerticalId, startt, endt).ToList<Article>();
            }
            catch (Exception ex)
            {
                logger.Error("VerticalCitationStore::initModelFromRepo() failed to fetch Articles from Pickscomb repository.");
                logger.Error(ex.Message);
                throw new Exception("VerticalCitationStore::initModelFromRepo() failed to fetch Articles from Pickscomb repository.");
            }

            msg = String.Format("Retrieved {0} Articles from the repository. Building the initial model . . .", articles.Count());
            logger.Info(msg);
            uint count = 0;
            foreach ( Article art in articles )
            {
                // Test function.
//                testArticleURLs(art);
                msg = string.Format( "Adding #[{0}]", ++count);
                logger.Info(msg);
                AddCitationsInArticle(art);
                //logger.Info("ADDED");
            }
            logger.Info("Initial CitationStore Model build complete.\n\n");

//            testCitationStore();
        }

        // Private test function for testing URLs extracted and stripped from Articles.
        private void testArticleURLs( Article art )
        {
            string msg;

            string summ = art.Summary.Trim();
            string text = art.Content.Trim();
            Guid aid = art.GetArticleId();

            if ( String.IsNullOrEmpty(text))
            {
                msg = String.Format("Article [{0}] has NULL/Empty content.\n\tSummary: {1}\n\tLink: {2}\n", 
                                        aid, summ, art.PermaLink );
                logger.Debug(msg);
            }
            else
            {
                if (Regex.IsMatch(text, @"href=", RegexOptions.IgnoreCase))
                {
                    msg = String.Format("Articles [{0}] has good Content and contains 'href=' URLs.", aid);
                }
                else if (Regex.IsMatch(text, @"http://", RegexOptions.IgnoreCase))
                {
                    msg = String.Format("Articles [{0}] has good Content and contains http:// URLs.", aid);
                }
                else
                {
                    msg = String.Format("Articles [{0}] has good Content and contains NO URLs or href.", aid);
                    logger.Debug(msg);
                    return;
                }
                logger.Debug(msg);


                List<string> urls = art.GetCitedLinks(false);
                List<string> murls = art.GetCitedLinks(true);
                if ( urls == null ) 
                {
                    logger.Debug("  GetCitedLinks() returned NO URLs.");
                }
                else
                {
                    string ustr = "  URLs:\n";
                    foreach ( string u in urls )
                    {
                        ustr = String.Format("  {0}  [{1}]\n", ustr, u);
                    }
                    ustr = String.Format( "{0}    Matching URLs*:\n", ustr);
                    foreach (string u in murls)
                    {
                        ustr = String.Format("  {0}    [{1}]\n", ustr, u);
                    }
                    msg = String.Format("GetCitedLinks() returned {0} URLs in text:\n{1}\n", 
                            urls.Count, ustr);
                    logger.Debug(msg);
                }
            }
        }


        // Private test fucntion for testing the status of the CitationStore Model for this Vertical.
        private void testCitationStore()
        {
            string msg;

            logger.Debug("~~~~~~~ Testing CitationStore Model. ~~~~~~~");

            if (_cstore.Count == 0)
            {
                logger.Debug("[VerticalCitationStore.testCitationStore()] CitationStore empty. Ending test.");
                return;
            }
            /*
            msg = String.Format("CitationStore has {0} URLs.\n", _cstore.Count());
            logger.Debug(msg);

            foreach ( string url in _cstore.Keys )
            {
                msg = String.Format("URL: {0}\n", url);
                foreach (DateTimeOffset dt in _cstore[url].Keys )
                {
                    msg = String.Format("{0}  [{1}] count = {2}\n", msg, dt, _cstore[url][dt]);
                }
                logger.Debug(msg);
            }
            */

            logger.Debug("\n\n\t------- Building the full Time Weighted CitationStore Model. -------");
            setTimeWeightedCitationPopularity();
            logger.Debug("Time Weighted CitationStore Model fully built.\n\n");
            
            msg = String.Format("\t------- Citation Model Scoring for {0} Articles. -------", _popularityScore.Count() );
            logger.Debug(msg);
            foreach ( KeyValuePair<string, double> p in _popularityScore )
            {
                
                msg = String.Format(" Score of [{0:F20}] for URL [{1}].", p.Value, p.Key);
                logger.Debug(msg);
            }
            logger.Debug("\t------- Citation Model Scored as above. -------\n\n");
        }


        /* Add single citation for a single URL */
        public void AddCitation(string url, DateTimeOffset timestamp)
        {
            string msg;
            try
            {
                if (_cstore.ContainsKey(url)) // store already contains citations for this URL
                {
                    if (_cstore[url].ContainsKey(timestamp))
                    {
                        msg = String.Format("(1) Adding to CStore[{0}][{1}] = {2}.", url, timestamp, _cstore[url][timestamp] +1);
                        logger.Debug(msg);
                        ++(_cstore[url][timestamp]); // multiple citation counts with the same timestamp
                        //logger.Debug("Added");
                    }
                    else
                    {
                        msg = String.Format("(2) Adding to CStore[{0}] timestamp [{1}] with count 1.", url, timestamp);
                        logger.Debug(msg);
                        _cstore[url].Add(timestamp, 1);   // single citation with this timestamp
                        //logger.Debug("Added");
                    }
                }
                else // store does not contain citations for this URL
                {
                     msg = String.Format("(3) Adding URL [{0}] to CStore timestamp [{1}] with count 1.", url, timestamp);
                     logger.Debug(msg);
                    SortedList<DateTimeOffset, uint> lst = new SortedList<DateTimeOffset, uint>();
                    lst.Add(timestamp, 1);
                    _cstore.Add(url, lst);
                    //logger.Debug("Added");
                }
            }
            catch ( Exception ex )
            {
                msg = String.Format("AddCitation( matchingUrl, timestamp) failed for [{0}, {1}].",
                                                url, timestamp);
                logger.Error(msg);
                logger.Error(ex.Message);
                //throw new Exception(msg);
            }
        }


        /* Add all the citations in this article to the citations store.
         * To run whenever a new Article is being added to the database. */
        public void AddCitationsInArticle(Article article)
        {
            string msg;

            List<string> urls = null;
            try
            {
                urls = article.GetCitedLinks(true);  // Use MatchingUrl format for citation comparison.
            }
            catch (Exception ex )
            {
                logger.Error("ERROR in article.GetCitedLinks().");
                throw ex;
            }
            //logger.Info("Got Cited URLs.");

            // Build the MatchingUrl <=> Article Guid index
            if (CitationStore.RepoArticleUrlGuidIndex[VerticalId].ContainsKey(article.MatchingUrl))
            {
                // skip this article
                return;
            }
            CitationStore.RepoArticleUrlGuidIndex[VerticalId].Add(article.MatchingUrl, article.GetArticleId());
            logger.Info("Updated master index.");

            if ( urls == null )
            {
                msg = String.Format("No Cited links found: 0 in this article [{0}]", article.GetArticleId());
                logger.Info(msg);
                return;
            }
            DateTimeOffset timestamp =  (DateTimeOffset)article.CreateDateTime;
            Guid aid = article.GetArticleId();
            
            msg = String.Format("Cited links found: {0} in this article [{1}]", urls.Count(), article.GetArticleId() );
            logger.Info(msg);
            foreach (string url in urls)
            {
                if ( !String.IsNullOrEmpty(url) )
                {
                    AddCitation( url, timestamp);
                }
            }
        }


        /* To run when an article is being removed from the database */
        public void RemoveCitationsInArticle(Article article)
        {
            List<string> urls = article.GetCitedLinks(true); // Use MatchingUrl format for citation comparison.
            DateTimeOffset timestamp = (DateTimeOffset)article.CreateDateTime;

            foreach (string url in urls)
            {
                if (_cstore.ContainsKey(url))
                {
                    if (_cstore[url].ContainsKey(timestamp))
                    {
                        if (--(_cstore[url][timestamp]) == 0) // no citations for this URL at this timestamp now
                        {
                            _cstore[url].Remove(timestamp);
                            if (_cstore[url].Count == 0)  // No other citations for this URL
                            {
                                _cstore.Remove(url);
                            }
                        }
                    }
                }
            }
        }

        public void clearAllCitations()
        {
            _cstore.Clear();
        }



        public void setTimeWeightedCitationPopularity(uint period)
        {
            Period = period;
            setTimeWeightedCitationPopularity();
        }


        public void setTimeWeightedCitationPopularity()
        {
            Dictionary<string, uint[]> cCountsPerURL = new Dictionary<string, uint[]>();   // per period citations counts for each URL
            uint[] totalCCounts;   // per period total citation counts

            _popularitySet = false;

            if (_cstore.Count == 0)
            {
                logger.ErrorFormat("[setTimeWeightedCitationPopularity()] CitationStore empty for vertical {0}.", 
                                VerticalConstants.GetFriendlyName(VerticalId) );
                return;
            }

            uint numPeriods = MaxHoursConsidered * 60 / Period;   // How many Period minutes in the Article lifetime of Max hours.
            if ((MaxHoursConsidered * 60) % Period != 0)          // One extra period if any time left over. 
            {
                ++numPeriods;
            }

            /* Count the citations per URL and total citations per each period. */
            List<DateTimeOffset> removeList;        // for cleaning up citations past the max hours limit
            uint p;                                 // processed period count
            DateTimeOffset now = DateTime.UtcNow;
            DateTimeOffset periodEnd;

            totalCCounts = new uint[numPeriods];
            foreach (KeyValuePair<string, SortedList<DateTimeOffset, uint>> i in _cstore)
            {
                cCountsPerURL[i.Key] = new uint[numPeriods];
                removeList = new List<DateTimeOffset>();

                // now count citations per url per period 
                // and total citations per period
                p = 0;  // processed period count
                periodEnd = now.AddMinutes(-1 * Period);
                foreach (KeyValuePair<DateTimeOffset, uint> j in i.Value)
                {
                    if (j.Key < periodEnd)    // Advance to next period backwards
                    {
                        ++p;
                        if (p > numPeriods)  // Mark this citations as past the last max allowed hours.
                        {
                            removeList.Add(j.Key);
                            continue;
                        }

                        periodEnd = periodEnd.AddMinutes(-1 * Period);
                    }

                    cCountsPerURL[i.Key][p] += j.Value;
                    totalCCounts[p] += j.Value;
                }

                // Now remove citations past the last 36 hour for this URL.
                foreach (DateTimeOffset dt in removeList)
                {
                    i.Value.Remove(dt);
                }
            }

            // Cleanup continued: Remove any URLs with no citations within the allwed Max hours.
            foreach (string url in _cstore.Keys)
            {
                if (_cstore[url].Count == 0)
                {
                    _cstore.Remove(url);
                }
            }

            /* Total and per-URL citation counts for each period are now set.
             * Citation store has also been cleaned of citations past the last 36 hours,
             * and of URL with no citations within the past 36 hours.
             * Now all URLs in citation store have citations within the past 36 hours.
             * Proceed to Popularity scoring.
             */

            // Get the correct time-weighting for each period.
            RecencyCalculator rec = new RecencyCalculator();
            double[] timeWeighting = rec.GetRecencyCurve(numPeriods);

            /* Estimate the Time Weighted Citation Based Popularity of each article. */
            Dictionary<string, double>  tmpp = new Dictionary<string, double>();
            foreach (KeyValuePair<string, uint[]> i in cCountsPerURL)
            {
                tmpp[i.Key] = 0.0;
                for (uint j = 0; j < numPeriods; ++j)
                {
                    if (totalCCounts[j] > 0)
                    {
                        if (cCountsPerURL[i.Key][j] > 0)
                        {
                            tmpp[i.Key] += 1000 * timeWeighting[j] * cCountsPerURL[i.Key][j] / totalCCounts[j];
                        }
                    }
                    else
                    {
                        if (cCountsPerURL[i.Key][j] > 0)
                        {
                            string msg = "[CitationStore::setTimeWeightedCitationPopularity()]"
                                + " Something went wrong for URL " + i.Key + " in time period " + j.ToString()
                                + "\n URL citation count " + cCountsPerURL[i.Key][j].ToString()
                                + " / total citation count " + totalCCounts[j].ToString();
                            logger.Error(msg);
                        }
                    }
                }
            }

            
            /*
            _averageScore = 0.0;
            foreach (KeyValuePair<string, double> i in _popularityScore)
            {
                _averageScore += i.Value;
            }
            _averageScore = _averageScore / _popularityScore.Count;
            */

            /* Now sort the Article URLs according to their popularity score. */
            _popularityScore.Clear();
            _popularityScore = tmpp.OrderByDescending(y => y.Value).ToDictionary(y => y.Key, y => y.Value);

            /*
            _popularityRankedURLs = new string[_popularityScore.Count];
            double maxScore = _popularityScore.ElementAt(0).Value;

            KeyValuePair<string, double> pr;
            for (int j = 0; j < _popularityScore.Count(); ++j )
            {
                pr = _popularityScore.ElementAt(j);
               // _popularityScore[pr.Key] = pr.Value; // un-normalized score
               // _popularityScore[pr.Key] = pr.Value / maxScore; // Mormalize to [0.0 .. 1.0] range
                _popularityRankedURLs[j] = pr.Key;
            }
             * */

            _popularitySet = true;
        }



        public double getTimeWeightedCitationPopularityScore(string url, uint period)
        {
            if (!_popularitySet)
            {
                setTimeWeightedCitationPopularity(period);
            }

            return _popularityScore[url];
        }


        public double getTimeWeightedCitationPopularityScore(string url)
        {
            if (!_popularitySet)
            {
                setTimeWeightedCitationPopularity(Period);
            }

            return _popularityScore[url];
        }


        public Dictionary<string, double> getTimeWeightedCitationPopularityScores(uint period)
        {
            if (!_popularitySet)
            {
                setTimeWeightedCitationPopularity(period);
            }

            return _popularityScore;
        }

        public Dictionary<string, double> getTimeWeightedCitationPopularityScores()
        {
            if (!_popularitySet)
            {
                setTimeWeightedCitationPopularity(Period);
            }

            return _popularityScore;
        }

/*
        public string[] getPopularURLs()
        {
            if (!_popularitySet)
            {
                setTimeWeightedCitationPopularity(Period);
            }

            return _popularityRankedURLs;
        }


        public string[] getPopularURLs(uint period)
        {
            if (!_popularitySet)
            {
                setTimeWeightedCitationPopularity(period);
            }

            return _popularityRankedURLs;
        }

 */
    }
}
