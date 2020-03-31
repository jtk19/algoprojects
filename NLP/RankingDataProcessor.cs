using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Net;

namespace ArtemisRank
{
    public class RankingDataProcessor
    {
        public static char Separator = '|'; // the field separator in a string to be stored in the DB

        /// <summary>
        /// Builds and returns the citations string in the format that is to be stored as the value 
        /// in the CITATIONS table of the database.
        /// </summary>
        /// <param name="articleCreateTime">Article.CreateTime timestamp</param>
        /// <param name="articleText">The text content of the Article</param>
        /// <returns>The string to be stored in the CITATIONS table of the database as the Value.</returns>
        /// 
        public static string GetCitationsDbString(  DateTimeOffset articleCreateTime, 
                                                    string articleURL,
                                                    string articleText )
        {
            StringBuilder dbStr = new StringBuilder( articleCreateTime.ToString() );

            dbStr.Append(Separator).Append(articleURL);

            List<string> citations = GetCitations(articleText, true);

            if ( citations.Count == 0 )
            {
                return null;
            }

            foreach( string url in citations )
            {
                dbStr.Append(Separator).Append(url);
            }

            return dbStr.ToString();
        }

        /// <summary>
        /// Converts and returns the citations from the database string that stores the citations
        /// in the CITATIONS table.
        /// </summary>
        /// <param name="dbStr">The databse value string from the CITATIONS table</param>
        /// <returns>The list of citations against the time they were sited.</returns>
        /// 
        public static KeyValuePair< DateTimeOffset, List<string> > GetCitationsFromDbString( 
                                                                        RankingType_T rankingType,
                                                                        string dbStr )
        {
            char[] delimiters = { Separator };

            string[] parts = dbStr.Split(delimiters);

            DateTimeOffset dt = DateTimeOffset.Parse(parts[0]);

            List<string> urls = new List<string>();

            if (rankingType == RankingType_T.Popularity)
            {
                for (int i = 1; i < parts.Length; ++i)
                {
                    urls.Add(parts[i]);
                }
            }
            else if ( rankingType == RankingType_T.PopularitySimilarity )
            {
                urls.Add(parts[1]); // this is the title Article's URL

                // get citations from raw text
                List<string> citations = RankingDataProcessor.GetCitations(parts[2], true);
                for (int i = 0; i < citations.Count; ++i)
                {
                    urls.Add(citations[i]);
                }
            }

            KeyValuePair<DateTimeOffset, List<string>> ct =
                new KeyValuePair<DateTimeOffset, List<string>>(dt, urls);

            return ct;
        }


        public static KeyValuePair< DateTimeOffset, string> GetSimilarityDataFromDbString(
                                                                        RankingType_T rankingType,
                                                                        string dbStr)
        {
            if ( rankingType != RankingType_T.PopularitySimilarity )
            {
                Ranker.Log.Error("[CitationProcessor.GetSimilarityDataFromDbString() failed. "
                            + " RankingType does not include Similarity Ranking.");
                return new KeyValuePair<DateTimeOffset, string>();
            }

            char[] delimiters = { Separator };

            string[] parts = dbStr.Split(delimiters);

            DateTimeOffset dt = DateTimeOffset.Parse(parts[0]);

            return new KeyValuePair<DateTimeOffset, string>( dt, parts[2]);
        }

        /// <summary>
        /// Get the list of Cigtations given the content text of an Article.
        /// </summary>
        /// <param name="articleText">The text content of an Article</param>
        /// <param name="asMatchingUrl">Whether to return the cleaned or raw URL (true => cleaned).</param>
        /// <returns></returns>
        public static List<string> GetCitations( string articleText, bool asMatchingUrl = true )
        {
            List<string> _links = null; 

            const string anchorRule = @"<a([^>]+)>.+?</a>";
            const string hrefRule = @"href=""(.+)""";

            if (!string.IsNullOrWhiteSpace(articleText))
            {

                _links = new List<string>();

                MatchCollection m1 = Regex.Matches(articleText, anchorRule, RegexOptions.Singleline | RegexOptions.IgnoreCase);
                foreach (Match m in m1)
                {
                    string value = m.Groups[1].Value.Trim();
                    var m2 = Regex.Match(value, hrefRule, RegexOptions.Singleline | RegexOptions.IgnoreCase);
                    string url = m2.Groups[1].Value.Trim();
                    if (!String.IsNullOrEmpty(url))
                    {
                        if (asMatchingUrl) // return the stripped and cleaned format URL
                        {
                            url = GetMatchingUrl(url);
                        }
                        else  // Only lengthen tiny-urls, but do not strip headers etc. otherwise
                        {
                            url = lengthenUrl(url);
                        }
                        if (!String.IsNullOrEmpty(url) && !_links.Contains(url))
                        {
                            _links.Add(url);
                        }
                    }
                }
            }

            return _links;
        }

        /// <summary>
        /// Is this a shortened URL (such as bit.ly)?
        /// </summary>
        /// 
        private static bool isShortURL(string url)
        {
            if (String.IsNullOrEmpty(url) || url.Length > 30)
            {
                return false;
            }

            string[] parts = Regex.Split(url, "/");
            if (parts.Count() > 4 || parts.Count() < 2)
            {
                return false;
            }

            int lastPart = parts.Count() - 1;
            if (parts[lastPart - 1].Length > 10  // checking host length
                || parts[lastPart].Length > 10) // the final encoding part
            {
                return false;
            }

            return true;
        }


        /// <summary>
        /// If this citation URL is a shortned URL (such as bit.ly) lengthen it.
        /// </summary>
        /// <param name="url"> Original URL</param>
        /// <returns>The lenghtned URL if it was in shortned format; the same one otherwise.</returns>
        ///
        private static string lengthenUrl(string url)
        {
            string longurl;
            //string msg;

            if (String.IsNullOrEmpty(url))
            {
                return String.Empty;
            }

            if (!isShortURL(url))
            {
                return url;
            }

            HttpWebRequest request = (HttpWebRequest)WebRequest.CreateHttp(url);
            request.Method = "HEAD";    // For efficiency, request only the header.
            request.AllowAutoRedirect = false;
            try
            {
                using (var response = request.GetResponse())
                {
                    var status = ((HttpWebResponse)response).StatusCode;
                    if (status == HttpStatusCode.Moved ||
                        status == HttpStatusCode.MovedPermanently)
                    {
                        longurl = response.Headers["Location"];
                        //msg = String.Format("[{0}] expanded to [{1}].", url, longurl);
                        //logger.Debug(msg);
                        return longurl;
                    }
                    //msg = String.Format("[{0}] has NO expansion.", url);
                    //logger.Debug(msg);
                    return url;
                }
            }
            catch (Exception)
            {
                return url; // Somehow could not lengthen; return the original.
            }
        }


        /// <summary>
        /// Given the original URL of an Article or from a cited link of an Article
        /// returns the base URL used to match or test for equality against each other.
        /// </summary>
        /// <param name="originalUrl">URL in format it occured.</param>
        /// <returns>Cleaned URLs with various extra things stripped.</returns>
        /// 
        public static string GetMatchingUrl(string originalUrl)
        {
            string url = String.Empty, regex;

            if (String.IsNullOrEmpty(originalUrl))
            {
                return String.Empty;
            }

            // First lengthen any shortened URLs, like bit.ly URLs. Then change case.
            url = lengthenUrl(originalUrl).ToLower();

            // Header: strip "http://", "www" etc.
            regex = @"^http://|^https://";
            url = Regex.Replace(url, regex, String.Empty, RegexOptions.Singleline).Trim();
            regex = @"^www.";
            url = Regex.Replace(url, regex, String.Empty, RegexOptions.Singleline).Trim();

            // Body character replacements
            url = Regex.Replace(url, @"(&amp;)", "&", RegexOptions.Singleline).Trim();
            url = Regex.Replace(url, @"(&nbsp;)|\+", "%20", RegexOptions.Singleline).Trim();    // map different spaces to %20
            url = Regex.Replace(url, @"\n|\r| ", String.Empty, RegexOptions.Singleline).Trim();

            // Page anchor removal
            url = Regex.Replace(url, "#.*$", String.Empty).Trim();

            // End "/" and end-space removal
            url = Regex.Replace(url, @"/$|(%20)+$", String.Empty).Trim();

            return url;
        }


    }
}
