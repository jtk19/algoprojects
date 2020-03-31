using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Net.Sockets;
using System.Net.Http;
using System.Net;
using System.IO;

using Common;
using WebAPI;


namespace ArtBase
{
    public class HTTPProcessor
    {
        public string http_url { get; set; }
        public string http_method { get; set; }
        public bool is_valid { get; set; }

        public string req_url { get; set; }
        public Topic req_topic { get; set; }


        private TcpClient _listener;
        private HTTPServer _server;

        public HTTPProcessor(TcpClient tlistener, HTTPServer srv)
        {
            _listener = tlistener;
            _server = srv;
        }

        public void process()
        {
            parseRequest();
            parseUrl();

            if ( http_url.Trim().ToUpper() == "WWW.STOP.COM" )
            {
                Common.Util.EndApplication = true;
                return;
            }

            if (!is_valid)
            {
                return;
            }

            if (http_method == "POST")
            {
                // This is considered a POST request to simply enter the Article
                // with the requested URL into the Article Database.

                WebAPI.WebAPI.AddArticle(req_topic, req_url);
            }
            else // http GET
            {
                // This is considered a request to return the top most simialr Article
                // We also enter the requested Article into the database.
                SortedDictionary<double, string> topN = GetSimilarArticles(req_topic, req_url, true);

                StreamWriter writer = new StreamWriter(_listener.GetStream());
                if (topN == null || topN.Count < 2 ) // something went wrong
                {
                    writer.WriteLine("Status: ERROR");
                    writer.WriteLine("Message: Could not fetch a similar Article");
                    writer.Flush();
                    writer.Close();
                }
                else
                {
                    // careful, topmost Article is likely to be the one the user
                    // queried with, with perfect similarity of 1
                    int sep_index = topN.ElementAt(1).Value.LastIndexOf('?');
                    string guid = topN.ElementAt(1).Value.Substring(sep_index+1).Trim();
                    string url = topN.ElementAt(1).Value.Substring(0, sep_index);

                    writer.WriteLine("Status: SUCCESS");
                    writer.WriteLine("Message: Similar Articles found.");
                    writer.WriteLine("URL: " + url);
                    writer.WriteLine("Similarity Score: " + topN.ElementAt(1).Key.ToString());

                    string dbfile = Common.Util.getArticleFileName(guid);
                    FileStream fs = new FileStream(dbfile, FileMode.Open);
                    StreamReader reader = new StreamReader(fs);
                    writer.Write(reader.ReadToEnd().ToString());
                    writer.Flush();
                    writer.Close();

                }
            }
        }

        private void parseRequest()
        {
            StreamReader reader = new StreamReader(_listener.GetStream());
            string request = reader.ReadLine();
            string[] tokens = request.Split(' ');
            if (tokens.Length != 3)
            {
                Console.WriteLine("[HTTPProcessor.parseRequest()] Invalid http request line: " + request);
                is_valid = false;
                return;
            }
            http_method = tokens[0].ToUpper().Trim();
            http_url = tokens[1];
            is_valid = true;
        }


        // URL is expected with "?[Topic]" appended to the end. e.g: topic "Books"
        // "http://www.newyorker.com/culture/cultural-comment/the-book-that-gets-inside-alfred-hitchcocks-mind?Books"
        private void parseUrl()
        {
            if (!is_valid || http_url == "")
            {
                return;
            }
            int div_pos = http_url.LastIndexOf('?');
            if (div_pos < 0)
            {
                req_topic = Topic.Unknown;
                req_url = http_url;
            }
            else
            {
                string tp = http_url.Substring(div_pos + 1);
                req_topic = Common.Util.getTopic(tp);
                req_url = http_url.Substring(0, div_pos);
            }

            if (!System.Uri.IsWellFormedUriString(req_url, UriKind.Absolute))
            {
                Console.WriteLine("[HTTPProcessor.parseUrl()] Invalid http URL: " + req_url);
                is_valid = false;
            }

        }


        public static SortedDictionary<double, string> GetSimilarArticles(Topic topic, string url,
                                                                           bool isNewArticle = true)
        {
            if (isNewArticle)
            {
                Common.AddRequestQueue.SendRequest(topic, url);
                while (!Common.AddRequestQueue.isEmpty())
                {
                    Thread.Sleep(2);    // Wait for Article Add to be processsed.
                }
            }

            return Logic.SimilarityRanker.Instance[topic].getTopSimilarArticles(topic, url, 2);
        }

    }
} 
