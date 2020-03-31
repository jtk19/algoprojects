using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArtemisRank
{
   
    public enum RankingType_T
    {
        Popularity = 0,
        PopularitySimilarity = 1
    }

    public class ArtcileIndexInfo
    {
        public string ArticleId { get; set; }
        public DateTimeOffset ArtcileDatetime { get; set; }
    }

    public interface IRankedArtcles
    {
    }

    public class PopularityRankedArticles : IRankedArtcles
    {
        public RankingType_T RType
        {
            get { return RankingType_T.Popularity; }
        }

        public SortedDictionary<double, string> PopularArticles =
                            new SortedDictionary<double, string>(new DescDuplicateDoubleComp());
    }

    public class PopularitySimilarityRankedArticles : IRankedArtcles
    {
        public RankingType_T RType
        {
            get { return RankingType_T.PopularitySimilarity; }
        }

        public SortedDictionary<double, PopularGroup> PopularAndSimilarArticles =
                            new SortedDictionary<double, PopularGroup>(new DescDuplicateDoubleComp());
    }


    public class PopularGroup
    {
        public string TitleArticle { get; set; }   // Article ID

        // similarity score, Article ID
        public SortedDictionary<double, string> SimilarArticles =
                            new SortedDictionary<double, string>(new DescDuplicateDoubleComp());
    }

    /// <summary>
    /// Comparer that sorts in the descending order and which allows duplicate keys.
    /// </summary>
    public class DescDuplicateDoubleComp : IComparer<double>
    {
        public int Compare(double x, double y)
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
    /// Comparer that sorts in the ascending order and which allows duplicate keys.
    /// </summary>
    public class AscDuplicateDoubleComp : IComparer<double>
    {
        public int Compare(double x, double y)
        {
            // use the default comparer to do the original comparison for doubles
            int ascendingResult = Comparer<double>.Default.Compare(x, y);

            if (ascendingResult == 0)  // allow duplicate keys by treating "equal" as "more than"
            {
                ascendingResult = 1;
            }

            return ascendingResult;
        }
    }


    /// <summary>
    /// Comparer that sorts in the ascending order by UTC DateTime and which allows duplicate keys.
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
    /// Comparer that sorts in the descending order by UTC DateTime and which allows duplicate keys.
    /// </summary>
    public class DescDuplicateDateTimeOffsetComp : IComparer<DateTimeOffset>
    {
        public int Compare(DateTimeOffset x, DateTimeOffset y)
        {
            int rtn;

            // use the default comparer to do the original comparison for doubles
            int ascendingResult = Comparer<DateTimeOffset>.Default.Compare(x, y);

            if (ascendingResult == 0)  // allow duplicate keys by treating "equal" as "less than"
            {
                rtn = -1;
            }
            else  // turn the normal ascending result
            {
                rtn = 0 - ascendingResult;
            }

            return rtn;
        }
    }

}
