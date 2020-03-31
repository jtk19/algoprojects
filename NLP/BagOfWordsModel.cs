using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;
using System.Collections;

using log4net;
using Model;
using Repository;
using Utilities;


// TO DO: Retrieve Logger just once and store.


namespace Logic
{

    /// <summary>
    /// Comparer that sorts in the descending order by UTC DateTime and which allows duplicate keys.
    /// </summary>
    public class AscDuplicateDateTimeOffsetComp : IComparer<DateTimeOffset>
    {
        public int Compare(DateTimeOffset x, DateTimeOffset y)
        {
            int rtn;

            // use the default comparer to do the original comparison for doubles
            int ascendingResult = Comparer<DateTimeOffset>.Default.Compare(x, y);

            if (ascendingResult == 0)  // allow duplicate keys by treating "equal" as "more than"
            {
                rtn = 1;
            }
            else  // turn the normal ascending result
            {
                rtn = ascendingResult;
            }

            return rtn;
        }
    }

    /// <summary>
    /// This model calculates accumulates and holds, first, the intermediate statistics 
    /// for TF-IDF. Then finally the actual TF-IDF value for each word.
    /// </summary>
    public class WordMetricVector
    {
        // For TF-IDF calculations, word level statistics 
        private SortedList<string,double> _wordvec;
        public SortedList<string, double> WordVec
            { get { return _wordvec; } }     

        private Guid _vid;      // The Vertical this model is for
        private Guid _artid;    // The Article ID this model is for
        public Guid ModelVerticalId { get { return _vid; } }
        public Guid ModelArticleId { get { return _artid; } }


        public WordMetricVector( Guid verticalId, Article art )
        {
            string w1;
            _wordvec = new SortedList<string, double>();

            _vid = verticalId;
            _artid = art.GetArticleId();

            string text = art.Content.ToLower();
            //string separators;
            string[] words = text.Split( (string[])null, StringSplitOptions.RemoveEmptyEntries);
            foreach ( string w in words )
            {
                if (filtered(w))
                {
                    w1 = cleanupWord(w);
                    if (w1 != String.Empty )
                    {
                        // Split again any words previously co-joined by tags and stray char etc.
                        string[] subwords = w1.Split((string[])null, StringSplitOptions.RemoveEmptyEntries);
                        foreach (string w2 in subwords)
                        {
                            if (_wordvec.ContainsKey(w2))
                            {
                                _wordvec[w2] += 1.0;
                            }
                            else
                            {
                                _wordvec.Add(w2, 1.0);
                            }
                        }
                    }
                }
            }

            double maxFreq = 0.0;
            foreach (string w in _wordvec.Keys)
            {
                if (_wordvec[w] > maxFreq)
                {
                    maxFreq = _wordvec[w];
                }
            }

            // Calculate Term Frequency
            // and update metrics for IDF
            SortedList<string, double> TF = new SortedList<string, double>();
            foreach (string w in _wordvec.Keys)
            {
                TF[w] = 0.5 + 0.5 * _wordvec[w] / maxFreq;      // Setting Term Frequency

                // Statistics for IDF. 
                // First, total count of articles containing each word in the vertical.
                if (BagOfWordsModel.NPerWord[_vid].ContainsKey(w))
                {
                    BagOfWordsModel.NPerWord[_vid][w] += 1;
                }
                else
                {
                    BagOfWordsModel.NPerWord[_vid].Add(w, 1);
                }

                /*// Set up the words for the AllWords Index
                try
                {
                    BagOfWordsModel.AllWordsIndex[_vid].Add(w, 0);
                }
                catch ( Exception)  // ignore if already added
                {}
                 * */
            }
            _wordvec.Clear();
            _wordvec = TF;

            // Set up the GUIDs for the Article-Guid index
            try
            {
                BagOfWordsModel.ArticleIndex[_vid].Add( (DateTimeOffset)art.CreateDateTime, art.GetArticleId());                
            }
            catch (Exception)  // ignore if already added
            { }

        } // end of constructor


        // TF-IDF calculation nust run after all Articles will have been added.
        // Must run again if a new Article is added to the repo.
        public void setTfIdf()
        {
            double idf;

            SortedList<string, double> IDF = new SortedList<string, double>();
            foreach (string w in _wordvec.Keys)
            {
                idf = Math.Log( BagOfWordsModel.N[_vid] / BagOfWordsModel.NPerWord[_vid][w]);
                IDF[w] = _wordvec[w] * idf;    // _wordvec already contains TF; so this is TF-IDF
            }
            _wordvec.Clear();
            _wordvec = IDF;
        }

        // Filters out any digit only or special chanracter only "words" above
        public static bool filtered( string s )
        {
            // Starts with an alphabetic character. 
            // Filter out digit only or special character only "words".
            return Regex.IsMatch(s, "^[a-z]{1}", RegexOptions.IgnoreCase) &&    // starts with an alphabetic character
                   (!Regex.IsMatch(s, "^http:", RegexOptions.IgnoreCase)) &&    // but ignore "http://" links
                   (!Regex.IsMatch(s, "^src=\"http:", RegexOptions.IgnoreCase)) && // ignore image links
                   (!Regex.IsMatch(s, "^href=\"http:", RegexOptions.IgnoreCase));  // ignore links
        }
        
        public static string cleanupWord( string s )
        {
            if ( Regex.IsMatch( s, "=", RegexOptions.IgnoreCase) )
            {
                return String.Empty;
            }

            s = Regex.Replace(s, @"<br>|</p>|</a>|</em>|</strong>|</b>|<p>|</big>", " ", RegexOptions.IgnoreCase);  // strip stray html tags
            s = Regex.Replace(s, "[.|,|:|;|!|\"]", " ", RegexOptions.IgnoreCase);   // strip stray punctuations
            s = Regex.Replace(s, @"'s|'re", String.Empty, RegexOptions.IgnoreCase);  // strip possessives and shortened forms

            return s.Trim();
        }
    }


    /// <summary>
    /// This model calculates Article Similarity for all Verticals 
    /// based on the Cosine Distance between bag-of-words TF-IDF vectors.
    /// </summary>
    public class BagOfWordsModel
    {
        public static BagOfWordsModel Instance = new BagOfWordsModel();

        private static ILog logger = Utilities.Logger.GetLogger(typeof(BagOfWordsModel));

        /// <summary>
        /// The lookup Index of Articles currently in consideration in the Model.
        /// The Model's Cosine Distance matrix is an R x R matrix indexed according to
        /// this, where R is the number of Articles in the Index for the particular
        /// Vertical. The top level Guid is that of the Vertical.
        /// </summary>
        public static Dictionary<Guid, SortedList<DateTimeOffset, Guid>> ArticleIndex; // List of all Articles sorted descending by their creation
                                                                                 // time, and each Article's Index ID in the list is its Index.
       
        // Below stats for the IDF statistic in TF-IDF
        public static Dictionary<Guid, Dictionary<string, uint>> NPerWord;     // Number of Articles containing each term (word) per vertical.
        public static Dictionary<Guid, uint> N;                                // Total number of Articles per vertical.

        public static void initStaticVar()
        {
            // The top level Guid is the Guid of the Vertical
            ArticleIndex = new Dictionary<Guid, SortedList< DateTimeOffset, Guid>>();

            NPerWord = new Dictionary<Guid, Dictionary<string, uint>>();
            N = new Dictionary<Guid, uint>();

            foreach (Guid vid in VerticalConstants.AllVerticals)
            {
                // This Index is sorted in the ascending order of each Article's publication date.
                // Thus the oldest Articles Guids are at the top, and we can delete them with ease
                // when their publication dates go out of the time range we consider.
                ArticleIndex[vid] = new SortedList<DateTimeOffset, Guid>( new AscDuplicateDateTimeOffsetComp() );

                NPerWord[vid] = new Dictionary<string, uint>();
                N[vid] = 0;
            }
            logger.Info("Initialized static variables.");
        }

        // Actual model for each particular vertical.
        private Dictionary<Guid, VerticalBagOfWordsModel> _vertBOWModel;
        public Dictionary<Guid, VerticalBagOfWordsModel> Model
        {
            get { return _vertBOWModel; }
        }


        public BagOfWordsModel()
        {
            BagOfWordsModel.initStaticVar();

            _vertBOWModel = new Dictionary<Guid, VerticalBagOfWordsModel>();
            foreach ( Guid vid in VerticalConstants.AllVerticals )
            {
                _vertBOWModel[vid] = new VerticalBagOfWordsModel(vid);
            }
            logger.Info("BagOfWordsModel initialized for all verticals.");
        }

        public void buildModelFromRepo()
        {
            foreach (Guid vid in VerticalConstants.AllVerticals)
            {
                _vertBOWModel[vid].buildModelFromRepo();   
            }
        }

        public void buildModelFromRepo( Guid verticalId )
        {
            _vertBOWModel[verticalId].buildModelFromRepo();
        }

    }


    /// <summary>
    /// This is the actual model per each Vertical. It calculates Article Similarity within
    /// each Vertical based on the Cosine Distance between the Bag-of-Words TF-IDF vectors
    /// between Articles.
    /// </summary>
    public class VerticalBagOfWordsModel
    {
        private static ILog logger = Utilities.Logger.GetLogger(typeof(VerticalBagOfWordsModel));

        public Guid VerticalId { get; set; }        // The Vertical for this model
        public uint RelatedTimeHours { get; set; }  // default time period to estimate Relatedness over

        SortedList<Guid, WordMetricVector> _ArticleTfIdfVec; // The primary TF-IDF Bag-of-Words vector Model
        private double[,] _cosineDistance;                   // The Cosine Distance Model beteween pairs of above vectors
        private bool[,] _isCalculated;                       // For just-in-time calculations of the Cosine Distance Model

        private bool _modelBuilt;

        private bool _runParallelVersion; 
        private CancellationTokenSource _cancellationToken;

        public bool BuildFullModelFromRepo;
        private bool _isFirstRun;           // For the first run we have to always build full from repo.
        private DateTime _lastRun;          // The EndTime for the last run.


        public VerticalBagOfWordsModel( Guid verticalId )
        {
            VerticalId = verticalId;
            RelatedTimeHours = 3 * 24; // default hours to fetch related Articles over
            _modelBuilt = false;
            _runParallelVersion = false;

            BuildFullModelFromRepo = false; // default
            _isFirstRun = true;             // Constructor being called means the first run.

            // Structure for Articles mapped to their TF-IDF Bag-of-Words Vectors.
            _ArticleTfIdfVec = new SortedList<Guid, WordMetricVector>();

            string msg = String.Format("Initialized Bag-of-Words Model for vertical {0}. \n\tBuilding initial Model from Pickscomb repository.",
                                            VerticalConstants.GetFriendlyName(verticalId));
            logger.Info(msg);

            VerticalBagOfWordsModel model = BagOfWordsModel.Instance.Model[verticalId];
            int rtn = model.buildModelFromRepo();
            if (rtn < 0)
            {
                logger.Error("[ERROR] VerticalBagOfWordsModel::buildModelFromRepo() for Model initialization failed.");
                throw new Exception("[ERROR] VerticalBagOfWordsModel::buildModelFromRepo() for Model initialization failed.");
            }
            msg = String.Format("[SUCCESS] BagOfWords Model created and built for Vertical {0}.\n", 
                            VerticalConstants.GetFriendlyName(verticalId));
            logger.Info(msg);
        }


        private double getCosineDistance(WordMetricVector wv1, WordMetricVector wv2)
        {
            double dist = 0.0;

            if (wv1.ModelArticleId == wv2.ModelArticleId)
            {
                return 1.0;    // same article, perfect "similarity"
            }

            ParallelOptions parallelOption = null;
            if (_runParallelVersion)
            {
                parallelOption = new ParallelOptions()
                {
                    MaxDegreeOfParallelism = System.Environment.ProcessorCount,
                    CancellationToken = _cancellationToken.Token
                };
            }

            SortedSet<string> wordUnion = new SortedSet<string>(wv1.WordVec.Keys);
            foreach (string w in wv2.WordVec.Keys)
            {
                if (_runParallelVersion)
                {
                    parallelOption.CancellationToken.ThrowIfCancellationRequested();
                }

                if (!wordUnion.Contains(w))
                {
                    wordUnion.Add(w);
                }
            }

            double[] v1 = new double[wordUnion.Count];
            double[] v2 = new double[wordUnion.Count];

            IEnumerable<string> v1Words = wv1.WordVec.Keys;
            IEnumerable<string> v2Words = wv2.WordVec.Keys;

            uint i = 0;
            foreach (string w in wordUnion)
            {
                if (_runParallelVersion)
                {
                    parallelOption.CancellationToken.ThrowIfCancellationRequested();
                }

                if (v1Words.Contains(w))
                {
                    v1[i] = wv1.WordVec[w];
                }
                // else defaults to 0.0

                if (v2Words.Contains(w))
                {
                    v2[i] = wv2.WordVec[w];
                }
                // else defaults to 0.0
                ++i;
            }

            double dotprod = 0.0, v1norm = 0.0, v2norm = 0.0;

            for (i = 0; i < wordUnion.Count; ++i)
            {
                if (_runParallelVersion)
                {
                    parallelOption.CancellationToken.ThrowIfCancellationRequested();
                }

                dotprod += v1[i] * v2[i];
                v1norm += v1[i] * v1[i];
                v2norm += v2[i] * v2[i];
            }
            dist = dotprod / (Math.Sqrt(v1norm) * Math.Sqrt(v2norm));

            return dist;
        }

        // For Model Buildtime optimizations. Deletes global IDF stats of Articles
        // out of the current time frame since the last run.
        private void removeWordTfIdfStats( Guid artguid  )
        {
            string msg;
            WordMetricVector wvec = _ArticleTfIdfVec[artguid];
            Guid vid = wvec.ModelVerticalId;

            foreach (string w in wvec.WordVec.Keys)
            {
                try
                {
                    // Verbose test and debugging logging removed from this block. Turn on again for debugging.

                    //msg = String.Format("NPerWord for word [{0}] and has score [{1}].", w, BagOfWordsModel.NPerWord[vid][w]);
                    //logger.Debug(msg);

                    BagOfWordsModel.NPerWord[vid][w] -= 1;
                    //msg = String.Format("Decremented score for word [{0}], score [{1}].", w, BagOfWordsModel.NPerWord[vid][w]);
                    //logger.Debug(msg);

                    if (BagOfWordsModel.NPerWord[vid][w] <= 0 ) 
                    {
                        BagOfWordsModel.NPerWord[vid].Remove(w);
                        //logger.Debug("Word also deleted.");
                    }
                }
                catch ( Exception ex )
                {
                    msg = String.Format("Failed on removing word [{0}].", w);
                    logger.Error(msg);
                    logger.Error(ex.Message);
                }
            }
        }

        // pure test function
        private void testArticleStats(List<Article> articles )
        {
            Guid art = Guid.Empty;
            uint len = 0;


            string msg = String.Format("******* Testing starting for the model for {0} articles.", articles.Count());
            logger.Debug(msg);

            foreach ( Article a in articles )
            {
                Guid aid = a.GetArticleId();
                WordMetricVector wv = _ArticleTfIdfVec[aid];
                msg = String.Format("** Article [{0}] had {1} length word model.", aid, wv.WordVec.Count());
                logger.Debug(msg);

                if (wv.WordVec.Count() > len )
                {
                    art = aid;
                    len = (uint)wv.WordVec.Count();
                }
                
                if ( wv.WordVec.Count() <= 0 )
                {
                    msg = String.Format("Article Summary:\n\t{0}", a.Summary);
                    logger.Debug(msg);

                    msg = String.Format("Article Text:\n\t{0}\n", a.Content);
                    logger.Debug(msg);
                }
                 
            }

            if ( art == Guid.Empty )
            {
                return;
            }

            msg = String.Format("\n******* Testing stats for Article with Guid [{0}].", art);
            logger.Debug(msg);

            WordMetricVector wvec = _ArticleTfIdfVec[art];
            logger.Debug("** WordmetricVector found.");

            Guid vid = wvec.ModelVerticalId;
            msg = String.Format( "** For vertical [{0}].", VerticalConstants.GetFriendlyName(vid));
            logger.Debug(msg);

            msg = String.Format("Staring test loop for [{0}] words. ", wvec.WordVec.Keys.Count());
            logger.Debug(msg);
            foreach (string w in wvec.WordVec.Keys)
            {
                try
                {
                    msg = String.Format("NPerWord deducted for word [{0}] and has score [{1}].", 
                                                    w, BagOfWordsModel.NPerWord[vid][w]);
                    logger.Debug(msg);
                    if (BagOfWordsModel.NPerWord[vid][w] <= 0 ) 
                    {
                        BagOfWordsModel.NPerWord[vid].Remove(w);
                        logger.Debug("Word also deleted due to zero count.");
                    }
                }
                catch (Exception ex)
                {
                    msg = String.Format("Failed on removing word [{0}].", w);
                    logger.Error(msg);
                    logger.Error(ex.Message);
                }
            }
        }

        // Main Model builder for the past RelatedTimeHours (36) hours 
        public int buildModelFromRepo()
        {
            // Default period from Now to going 36 hours back.
            DateTime endt = DateTime.UtcNow;
            DateTime startt = DateTime.UtcNow.AddHours(-1 * RelatedTimeHours);

            _runParallelVersion = false;

            return buildModelFromRepo(startt, endt);
        }


        // Disabled for public access for now. Model built only from Now  
        // going back RelatedTimeHours hours.
        private int buildModelFromRepo( DateTime startTime, DateTime endTime )
        {
            string msg;

            _modelBuilt = false;
            _runParallelVersion = false;
            
            // basic sanity checks
            if ( startTime >= endTime )
            {
                msg = String.Format( "buildModelFromRepo(): startTime [{0}] must be less than endTime [{1}].",
                                startTime.ToString(), endTime.ToString() );
                logger.Error(msg);
                msg = String.Format("Building Model for default time, from Now going back {0} hours.",
                                RelatedTimeHours);
                logger.Error(msg);
                return buildModelFromRepo();
            }
            msg = String.Format( "buildModelFromRepo(): Building Model for Articles\n\t from startTime [{0}] to endTime [{1}].",
                                startTime.ToString(), endTime.ToString() );
            logger.Info(msg);

            // Article retrieval and modal adjustments for expired Articles
            logger.Debug("Retrieving Articles from Pickscomb repository.");
            List<Article> articles = null;
            try
            {
                IPickscombRepository repo = PickscombRepository.Instance;
                logger.Info("PickscombRepository instantiated. Fetching Articles . . .");

                if ( BuildFullModelFromRepo || _isFirstRun) // Load all aArticles for the whole timeframe from the repo.
                {
                    articles = repo.GetArticles(VerticalId, startTime, endTime).ToList<Article>();
                    if (articles.Count() < 2)
                    {
                        string err = String.Format("Model needs 2 or more Articles to build. {0} is not enough.",
                                        articles.Count());
                        logger.Error(err);
                        return -1;
                    }

                    msg = String.Format("Retrieved {0} Articles from Pickscomb repository.", articles.Count());
                    logger.Debug(msg);

                }
                else // Keep Articles within the time range from the last run; build only the remainder.
                {
                    logger.Debug("Adjusting the Model relative to the last run.");

                    // First delete the expired Articles outside the time range for the new Model.
                    logger.Debug("Removing the Expired Articles.");
                    uint deleteDocCount = 0;
                    foreach ( KeyValuePair<DateTimeOffset, Guid> p in BagOfWordsModel.ArticleIndex[VerticalId] )
                    {
                        if ( p.Key < startTime )
                        {
                            msg = String.Format("  Removing Article [{0}] dated [{1}].", p.Value, p.Key);
                            logger.Debug(msg);
                            removeWordTfIdfStats(p.Value);
                            logger.Debug("  Removed TF-IDF stats.");
                            _ArticleTfIdfVec.Remove(p.Value);
                            logger.Debug("  Removed Article from TF-IDF Vector Model.");
                            ++deleteDocCount;
                        }
                        else
                        {
                            break; // Since it is a list sorted in the ascending order by date 
                                   // you will find no more Articles with dates out-of-time.
                        }
                    }

                    // Now delete expiring Articles from the Indexes
                    msg = String.Format("Deleting {0} expired Articles from the Index.", deleteDocCount);
                    logger.Debug(msg);
                    for (uint i = 0; i < deleteDocCount; ++i )
                    {
                        Guid removingId = BagOfWordsModel.ArticleIndex[VerticalId].ElementAt(0).Value;
                        BagOfWordsModel.ArticleIndex[VerticalId].RemoveAt(0);

                        // search for value (URL) key in the (MarchingUrl, Guid) index
                        string url = CitationStore.RepoArticleUrlGuidIndex[VerticalId].FirstOrDefault(x => x.Value == removingId).Key;
                        CitationStore.RepoArticleUrlGuidIndex[VerticalId].Remove(url);
                    }
                    msg = String.Format("Model adjustments: {0} expired Articles removed.", deleteDocCount);
                    logger.Debug(msg);

                    // Now read from the repo only any Articles new since the last run.
                    articles = repo.GetArticles(VerticalId, _lastRun, endTime).ToList<Article>();
                    msg = String.Format("Model adjustments: {0} new Articles read since the last run.", 
                                            articles.Count() );
                    logger.Debug(msg);
                }
            }
            catch (Exception ex)
            {
                logger.Error("VerticalBagOfWordsModel::buildModelFromRepo() failed.");
                logger.Error(ex.Message);
                return -2;
            }

            Logger.GetLogger(typeof(VerticalBagOfWordsModel)).Debug("Building Bag-of-Word Vector Model.");
            foreach ( Article art in articles )
            {
                try
                {
                    _ArticleTfIdfVec.Add(art.GetArticleId(), new WordMetricVector(VerticalId, art));
                }
                catch ( ArgumentNullException )
                {
                    // should not happen
                }
                catch (ArgumentException) // Duplicate Article
                {
                    // Rebuild any dupicate Articles because the Article content might have changed
                    removeWordTfIdfStats(art.GetArticleId());
                    _ArticleTfIdfVec.Remove(art.GetArticleId());
                    int index = BagOfWordsModel.ArticleIndex[VerticalId].IndexOfValue(art.GetArticleId());
                    BagOfWordsModel.ArticleIndex[VerticalId].RemoveAt(index);
                    // now add again
                    _ArticleTfIdfVec.Add(art.GetArticleId(), new WordMetricVector(VerticalId, art));
                }
            }

            if (BuildFullModelFromRepo || _isFirstRun)
            {
                logger.Debug("WordMetricVectors built for each Article from the repository.");
            }
            else
            {
                logger.Debug("WordMetricVectors updated for the new timeframe from the repository.");
            }

            BagOfWordsModel.N[VerticalId] = (uint)_ArticleTfIdfVec.Count();
            msg = String.Format("The total TF-IDF bag-of-Words Vector Count in Model [{0}].\n",
                                    BagOfWordsModel.N[VerticalId]);
            logger.Debug(msg);

            // Pure test fuction to run only for testing and debugging.
            // Logger.GetLogger(typeof(VerticalBagOfWordsModel)).Debug("Testing Stats for Article at index 199.");
            // testArticleStats( articles);
            
            // recover space not needed
            articles.Clear();


            Logger.GetLogger(typeof(VerticalBagOfWordsModel)).Debug("Setting TF-IDF statistics.");
            foreach ( Guid artid in _ArticleTfIdfVec.Keys )
            {
                _ArticleTfIdfVec[artid].setTfIdf();
            }
            logger.Info("TF-IDF metrics set in the Model.");


            /*
            // Old version of algorithm, now optimised.
            int cols = BagOfWordsModel.AllWordsIndex[VerticalId].Count;
            double[,] tfidf_matrix = new double[rows, cols];

            Guid guid;
            string w;
            double val;

            for (int r = 0; r < rows; ++r )
            {
                guid = BagOfWordsModel.ArticleIndex[VerticalId].ElementAt(r).Key;

                for (int c = 0; c < cols; ++c )
                {
                    w = BagOfWordsModel.AllWordsIndex[VerticalId].ElementAt(c).Key;
                    ArticleTfIdfVec[guid].WordVec.TryGetValue(w, out val);      // If w not in vec, defaults to 0.0
                    tfidf_matrix[r, c] = val;
                }
            }

            // Recover more space from metrics not needed any more.
            ArticleTfIdfVec.Clear();
             

            // Now calculate the Cosine Distance between Articles
            _cosineDistance = new double[rows, rows];
            double dotprod, v1norm, v2norm;

            for ( int r1 =0; r1 < rows; ++r1 )
            {
                v1norm = 0.0;
                for ( int c = 0; c < cols; ++c )
                {
                    v1norm += tfidf_matrix[r1, c] * tfidf_matrix[r1, c];
                }
                // Optimising for _cosineDistance[r1,r2] == _cosineDistance[r2, r1]
                for ( int r2 =0; r2 < r1; ++r2 )
                {
                    dotprod = 0.0;
                    v2norm = 0.0;
                    for ( int c = 0; c < cols; ++c )
                    {
                        dotprod += tfidf_matrix[r1, c] * tfidf_matrix[r2, c];
                        v2norm += tfidf_matrix[r2, c] * tfidf_matrix[r2, c];
                    }
                    _cosineDistance[r1, r2] = dotprod / (Math.Sqrt(v1norm) * Math.Sqrt(v2norm));
                }
            }
            */

            // Now calculate the Cosine Distance between Articles
            // int rows = BagOfWordsModel.ArticleIndex[VerticalId].Count;
            int rows = _ArticleTfIdfVec.Keys.Count;
            int rows1 = BagOfWordsModel.ArticleIndex[VerticalId].Count;
            _cosineDistance = new double[rows, rows];   // initialized to default, 0.0
            _isCalculated = new bool[rows, rows];       // initialized to default, false

            /* -----------------------------------------------------
            //  Moving to "Just in time" model population
            Guid gid1, gid2;

            // _cosineDistance is an r x r matrix where r is the number of Documents
            // within the time frame considered - currently 36 hours.
            // This matrix is symmetric around the diagonal such that
            // _cosineDistance( r1, r2 ) == _cosineDistance( r2, r1 ).
            // Also the diagonal holds the similarity of a document to itself and 
            // is therefore valued 1.0. i.e:
            // _cosineDistance( r, r ) = 1.0;
            // Thereforem, to optimize, only the bottom half of the matrix is populated
            // such that r2 < r1 for all values.
            for (int r1 = 0; r1 < rows; ++r1 )
            {
                gid1 = BagOfWordsModel.ArticleIndex[VerticalId].Values[r1];
                for (int r2 =0; r2 < r1; ++r2 )
                {
                    gid2 = BagOfWordsModel.ArticleIndex[VerticalId].Values[r2];
                    _cosineDistance[r1, r2] = getCosineDistance(_ArticleTfIdfVec[gid1], _ArticleTfIdfVec[gid2]);
                }
            }
            Logger.GetLogger(typeof(VerticalBagOfWordsModel)).Info(
                "Article Cosine Distances between TF-IDF Bag-of-Words Vectors set in the Model.");
            */
           
            /*
            // Test Articles Index
            Logger.GetLogger(typeof(VerticalBagOfWordsModel)).Debug("TESTING Article Index:");
            uint x = 0;
            foreach (KeyValuePair<DateTimeOffset,Guid> p in BagOfWordsModel.ArticleIndex[VerticalId] )
            {
                if (x == 40) { break; }
                msg = String.Format("  Article Index: Date [{0}] Guid [{1}].", p.Key, p.Value);
                Logger.GetLogger(typeof(VerticalBagOfWordsModel)).Debug(msg);
            }
            */

            msg = String.Format("Model matrices initialized for {0} rows (Articles) == {1} rows (Article Index).", rows, rows1);
            logger.Debug(msg);
            logger.Info("Model Initialization complete.\n");

            _modelBuilt = true;
            _isFirstRun = false;
            _lastRun = endTime;

            return 0;
        }


        // Parraleloptions version
        public int buildModelFromRepo(CancellationTokenSource cancellationToken )
        {
            DateTime endt = DateTime.UtcNow;
            DateTime startt = DateTime.UtcNow.AddHours(-1 * RelatedTimeHours);

            _runParallelVersion = true;

            return buildModelFromRepo(startt, endt, cancellationToken );
        }

        // Parraleloptions version
        private int buildModelFromRepo(DateTime startTime, DateTime endTime, 
                                        CancellationTokenSource cancellationToken)
        {
            string msg;

            _modelBuilt = false;

            _runParallelVersion = true;
            _cancellationToken = cancellationToken;

            // basic sanity checks
            if (startTime >= endTime)
            {
                msg = String.Format("buildModelFromRepo(): startTime [{0}] must be less than endTime [{1}].",
                                startTime.ToString(), endTime.ToString());
                logger.Error(msg);
                msg = String.Format("Building Model for default time, from Now going back {0} hours.",
                                RelatedTimeHours);
                logger.Error(msg);
                return buildModelFromRepo();
            }
            msg = String.Format("buildModelFromRepo(): Building Model for Articles\n\t from startTime [{0}] to endTime [{1}].",
                                startTime.ToString(), endTime.ToString());
            logger.Info(msg);


            var parallelOption = new ParallelOptions()
            {
                MaxDegreeOfParallelism = System.Environment.ProcessorCount,
                CancellationToken = cancellationToken.Token
            };

            // Article retrieval and modal adjustments for expired Articles
            logger.Debug("Retrieving Articles from Pickscomb repository.");
            List<Article> articles = null;
            try
            {
                IPickscombRepository repo = PickscombRepository.Instance;
                logger.Info("PickscombRepository instantiated.");

                if (BuildFullModelFromRepo || _isFirstRun) // Load all aArticles for the whole timeframe from the repo.
                {
                    articles = repo.GetArticles(VerticalId, startTime, endTime).ToList<Article>();
                    if (articles.Count() < 2)
                    {
                        string err = String.Format("Model needs 2 or more Articles to build. {0} is not enough.",
                                        articles.Count());
                        logger.Error(err);
                        return -1;
                    }

                    msg = String.Format("Retrieved {0} Articles from Pickscomb repository.", articles.Count());
                    logger.Debug(msg);

                }
                else // Keep Articles within the time range from the last run; build only the remainder.
                {
                    logger.Debug("Adjusting the Model relative to the last run.");

                    // First delete the expired Articles outside the time range for the new Model.
                    logger.Debug("Removing the Expired Articles.");
                    uint deleteDocCount = 0;
                    foreach (KeyValuePair<DateTimeOffset, Guid> p in BagOfWordsModel.ArticleIndex[VerticalId])
                    {
                        parallelOption.CancellationToken.ThrowIfCancellationRequested();

                        if (p.Key < startTime)
                        {
                            msg = String.Format("  Removing Article [{0}] dated [{1}].", p.Value, p.Key);
                            logger.Debug(msg);
                            removeWordTfIdfStats(p.Value);
                            logger.Debug("  Removed TF-IDF stats.");
                            _ArticleTfIdfVec.Remove(p.Value);
                            logger.Debug("  Removed Article from TF-IDF Vector Model.");
                            ++deleteDocCount;
                        }
                        else
                        {
                            break; // Since it is a list sorted in the ascending order by date 
                            // you will find no more Articles with dates out-of-time.
                        }
                    }
                    // Now delete expiring Articles from the Indexes
                    msg = String.Format("Deleting {0} expired Articles from the Index.", deleteDocCount);
                    logger.Debug(msg);
                    for (uint i = 0; i < deleteDocCount; ++i)
                    {
                        parallelOption.CancellationToken.ThrowIfCancellationRequested();

                        Guid removingId = BagOfWordsModel.ArticleIndex[VerticalId].ElementAt(0).Value;
                        BagOfWordsModel.ArticleIndex[VerticalId].RemoveAt(0);

                        // search for value (URL) key in the (MarchingUrl, Guid) index
                        string url = CitationStore.RepoArticleUrlGuidIndex[VerticalId].FirstOrDefault(x => x.Value == removingId).Key;
                        CitationStore.RepoArticleUrlGuidIndex[VerticalId].Remove(url);
                    }

                    msg = String.Format("Model adjustments: {0} expired Articles removed.", deleteDocCount);
                    logger.Debug(msg);

                    // Now read from the repo only any Articles new since the last run.
                    articles = repo.GetArticles(VerticalId, _lastRun, endTime).ToList<Article>();
                    msg = String.Format("Model adjustments: {0} new Articles read since the last run.",
                                            articles.Count());
                    logger.Debug(msg);
                }
            }
            catch (Exception ex)
            {
                logger.Error("VerticalBagOfWordsModel::buildModelFromRepo() failed.");
                logger.Error(ex.Message);
                return -2;
            }


            Logger.GetLogger(typeof(VerticalBagOfWordsModel)).Debug("Building Bag-of-Word Vector Model.");
            foreach (Article art in articles)
            {
                parallelOption.CancellationToken.ThrowIfCancellationRequested();

                try
                {
                    _ArticleTfIdfVec.Add(art.GetArticleId(), new WordMetricVector(VerticalId, art));
                }
                catch (ArgumentNullException)
                {
                    // should not happen
                }
                catch (ArgumentException) // Duplicate Article
                {
                    // Rebuild any dupicate Articles because the Article content might have changed
                    removeWordTfIdfStats(art.GetArticleId());
                    _ArticleTfIdfVec.Remove(art.GetArticleId());
                    int index = BagOfWordsModel.ArticleIndex[VerticalId].IndexOfValue(art.GetArticleId());
                    BagOfWordsModel.ArticleIndex[VerticalId].RemoveAt(index);
                    // now add again
                    _ArticleTfIdfVec.Add(art.GetArticleId(), new WordMetricVector(VerticalId, art));
                }
            }

            if (BuildFullModelFromRepo || _isFirstRun)
            {
                logger.Debug("WordMetricVectors built for each Article from the repository.");
            }
            else
            {
                logger.Debug("WordMetricVectors updated for the new timeframe from the repository.");
            }

            BagOfWordsModel.N[VerticalId] = (uint)_ArticleTfIdfVec.Count();
            msg = String.Format("The total TF-IDF Bag-of-Words Vector Count in Model [{0}].\n",
                                    BagOfWordsModel.N[VerticalId]);
            logger.Debug(msg);

            // Pure test fuction to run only for testing and debugging.
            // Logger.GetLogger(typeof(VerticalBagOfWordsModel)).Debug("Testing Stats for Article at index 199.");
            // testArticleStats( articles);

            // recover space not needed
            articles.Clear();


            logger.Debug("Setting TF-IDF statistics.");
            foreach (Guid artid in _ArticleTfIdfVec.Keys)
            {
                parallelOption.CancellationToken.ThrowIfCancellationRequested();

                _ArticleTfIdfVec[artid].setTfIdf();
            }
            logger.Info("TF-IDF metrics set in the Model.");


            /*
            // Old version of algorithm, now optimised.
            int cols = BagOfWordsModel.AllWordsIndex[VerticalId].Count;
            double[,] tfidf_matrix = new double[rows, cols];

            Guid guid;
            string w;
            double val;

            for (int r = 0; r < rows; ++r )
            {
                guid = BagOfWordsModel.ArticleIndex[VerticalId].ElementAt(r).Key;

                for (int c = 0; c < cols; ++c )
                {
                    w = BagOfWordsModel.AllWordsIndex[VerticalId].ElementAt(c).Key;
                    ArticleTfIdfVec[guid].WordVec.TryGetValue(w, out val);      // If w not in vec, defaults to 0.0
                    tfidf_matrix[r, c] = val;
                }
            }

            // Recover more space from metrics not needed any more.
            ArticleTfIdfVec.Clear();
             

            // Now calculate the Cosine Distance between Articles
            _cosineDistance = new double[rows, rows];
            double dotprod, v1norm, v2norm;

            for ( int r1 =0; r1 < rows; ++r1 )
            {
                v1norm = 0.0;
                for ( int c = 0; c < cols; ++c )
                {
                    v1norm += tfidf_matrix[r1, c] * tfidf_matrix[r1, c];
                }
                // Optimising for _cosineDistance[r1,r2] == _cosineDistance[r2, r1]
                for ( int r2 =0; r2 < r1; ++r2 )
                {
                    dotprod = 0.0;
                    v2norm = 0.0;
                    for ( int c = 0; c < cols; ++c )
                    {
                        dotprod += tfidf_matrix[r1, c] * tfidf_matrix[r2, c];
                        v2norm += tfidf_matrix[r2, c] * tfidf_matrix[r2, c];
                    }
                    _cosineDistance[r1, r2] = dotprod / (Math.Sqrt(v1norm) * Math.Sqrt(v2norm));
                }
            }
            */

            // Now calculate the Cosine Distance between Articles
            // int rows = BagOfWordsModel.ArticleIndex[VerticalId].Count;
            int rows = _ArticleTfIdfVec.Keys.Count;
            int rows1 = BagOfWordsModel.ArticleIndex[VerticalId].Count;
            _cosineDistance = new double[rows, rows];   // initialized to default, 0.0
            _isCalculated = new bool[rows, rows];       // initialized to default, false

            /* -----------------------------------------------------
            //  Moving to "Just in time" model population
            Guid gid1, gid2;

            // _cosineDistance is an r x r matrix where r is the number of Documents
            // within the time frame considered - currently 36 hours.
            // This matrix is symmetric around the diagonal such that
            // _cosineDistance( r1, r2 ) == _cosineDistance( r2, r1 ).
            // Also the diagonal holds the similarity of a document to itself and 
            // is therefore valued 1.0. i.e:
            // _cosineDistance( r, r ) = 1.0;
            // Thereforem, to optimize, only the bottom half of the matrix is populated
            // such that r2 < r1 for all values.
            for (int r1 = 0; r1 < rows; ++r1 )
            {
                gid1 = BagOfWordsModel.ArticleIndex[VerticalId].Values[r1];
                for (int r2 =0; r2 < r1; ++r2 )
                {
                    gid2 = BagOfWordsModel.ArticleIndex[VerticalId].Values[r2];
                    _cosineDistance[r1, r2] = getCosineDistance(_ArticleTfIdfVec[gid1], _ArticleTfIdfVec[gid2]);
                }
            }
            Logger.GetLogger(typeof(VerticalBagOfWordsModel)).Info(
                "Article Cosine Distances between TF-IDF Bag-of-Words Vectors set in the Model.");
            */

            /*
            // Test Articles Index
            Logger.GetLogger(typeof(VerticalBagOfWordsModel)).Debug("TESTING Article Index:");
            uint x = 0;
            foreach (KeyValuePair<DateTimeOffset,Guid> p in BagOfWordsModel.ArticleIndex[VerticalId] )
            {
                if (x == 40) { break; }
                msg = String.Format("  Article Index: Date [{0}] Guid [{1}].", p.Key, p.Value);
                Logger.GetLogger(typeof(VerticalBagOfWordsModel)).Debug(msg);
            }
            */

            msg = String.Format("Model matrices initialized for {0} rows (Articles) == {1} rows (Article Index).", rows, rows1);
            logger.Debug(msg);
            logger.Info("Model Initialization complete.\n");

            _modelBuilt = true;
            _isFirstRun = false;
            _lastRun = endTime;

            return 0;
        }



        public double getCosineDistance( Guid article1, Guid article2 )
        {
            double dist = 0.0;

            if (!_modelBuilt)
            {
                if ( buildModelFromRepo() < 0 )
                {
                    string err = "VerticalBagOfWordsModel::getCosineDistance(art1, art2) failed. Model failed to build.";
                    logger.Error(err);
                    throw new Exception(err);
                }
            }

            int rows = _ArticleTfIdfVec.Keys.Count;
            int r1 = BagOfWordsModel.ArticleIndex[VerticalId].IndexOfValue(article1);
            int r2 = BagOfWordsModel.ArticleIndex[VerticalId].IndexOfValue(article2);

            if (r1 == r2)
            {
                dist = 1.0;
            }
            else {
                
                if ( r1 < r2 )
                {
                    // Optimising for _cosineDistance[r1,r2] == _cosineDistance[r2, r1]
                    // Swap, because only half the matrix is calculated to optimise.
                    int tmp = r2;
                    r2 = r1;
                    r1 = tmp;
                }
            
                if ( _isCalculated[r1, r2])
                {
                    dist = _cosineDistance[r1, r2];
                }
                else
                {
                    Guid gid1 = BagOfWordsModel.ArticleIndex[VerticalId].Values[r1];
                    Guid gid2 = BagOfWordsModel.ArticleIndex[VerticalId].Values[r2];
                    dist = getCosineDistance(_ArticleTfIdfVec[gid1], _ArticleTfIdfVec[gid2]);
                }
                
            }
            return dist;
        }

        // Returns the list of all Articles sorted in the descending order of 
        // similarity to "article" - them most similar Articles first.
        // Note the first Aarticle, with a score of 1, will be the passed
        // in Article itself with perfect "similarity". Other scores vary 
        // between [0.0..1.0] with higher scored Articles the most similar.
        public SortedList<double, Guid> getCosineDistanceFrom( Guid article )
        {
            int rtn;

            if (!_modelBuilt)
            {
                if ( _runParallelVersion )
                {
                    rtn = buildModelFromRepo(_cancellationToken);
                }
                else
                {
                    rtn = buildModelFromRepo();
                }
                if ( rtn < 0)
                {
                    string err = "VerticalBagOfWordsModel::getCosineDistance(article) failed. Model failed to build.";
                    logger.Error(err);
                    throw new Exception(err);
                }
            }

            ParallelOptions parallelOptions = null;
            if (_runParallelVersion)
            {
                parallelOptions = new ParallelOptions()
                {
                    MaxDegreeOfParallelism = System.Environment.ProcessorCount,
                    CancellationToken = _cancellationToken.Token
                };
            }

            SortedList<double, Guid> articleSimilarityVec = new SortedList<double, Guid>(new DescDoubleComp());

            int r1 = BagOfWordsModel.ArticleIndex[VerticalId].IndexOfValue(article);
            int rows = _ArticleTfIdfVec.Keys.Count;
            int r;
            Guid gid1, gid2;
            double dist = 0.0;

            for (int r2 = 0; r2 < rows; ++r2 )
            {
                if (_runParallelVersion)
                {
                    parallelOptions.CancellationToken.ThrowIfCancellationRequested();
                }

                if (r1 == r2)
                {
                    dist = 1.0;
                }
                else
                {
                    if (r1 < r2)
                    {
                        // Optimising for _cosineDistance[r1,r2] == _cosineDistance[r2, r1]
                        // Swap, because only half the matrix is calculated to optimise.
                        r = r1;
                        r1 = r2;
                    }
                    else // r1 > r2
                    {
                        r = r2;
                    }

                    if (!_isCalculated[r1, r])
                    {
                        gid1 = BagOfWordsModel.ArticleIndex[VerticalId].Values[r1];
                        gid2 = BagOfWordsModel.ArticleIndex[VerticalId].Values[r];
                        _cosineDistance[r1, r] = getCosineDistance(_ArticleTfIdfVec[gid1], _ArticleTfIdfVec[gid2]);
                        _isCalculated[r1, r] = true;
                    }
                    dist = _cosineDistance[r1, r];
                }

                if ( !Double.IsNaN(dist)  &&  (dist > 0.5) ) // only fetch those with Similarity > 0.5
                {
                    articleSimilarityVec.Add(dist,
                        BagOfWordsModel.ArticleIndex[VerticalId].ElementAt(r2).Value);
                }
            }

            return articleSimilarityVec;
        }

        // Returns the list of all Articles sorted in the descending order of 
        // similarity to "article" - them most similar Articles first.
        // Note the first Aarticle, with a score of 1, will be the passed
        // in Article itself with perfect "similarity". Other scores vary 
        // between [0.0..1.0] with higher scored Articles the most similar.
        public SortedList<double, Guid> getSimilarArticles( Guid article )
        {
            return getCosineDistanceFrom(article);
        }

    }
}
