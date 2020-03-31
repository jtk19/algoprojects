using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ArtemisRank
{
    public class RecencyWeightingCurve
    {

        /* Time weighting curve with expoenential deacay.
         * [1.0, 0.12] range approximately reserved for the first 36 hours.
         * The last 0.12 decimal decay apply to citations older than 36 hours */
        private double[] _curve;
        public double[] GetRecencyCurve(uint numPeriods)
        {
            _curve = new double[numPeriods];
            double decayRate;

            if (numPeriods > 127) // Decay with 15 minute periods - lower limit
            {
                decayRate = 0.0153;  // For decay over 36 * 60 /15 = 144 values
                // exp( -rt ) = 0.0273237 for t = 144
            }
            else if (numPeriods > 98) // decay over 36 * 60 / 20 minutes = 108 periods
            {
                decayRate = 0.0204;
            }
            else if (numPeriods > 66) // decay over 36 * 60 / 30 minutes = 72 periods
            {
                decayRate = 0.0306;
            }
            else if (numPeriods > 51)  // decay over ~54 periods ( ~40 min period)
            {
                decayRate = 0.0408;
            }
            else if (numPeriods > 34)    // decay over ~36 periods ( ~60 min periods )
            {
                decayRate = 0.0611;
            }
            else if (numPeriods > 26)    // decay over ~27 periods 
            {
                decayRate = 0.075;
            }
            else if (numPeriods > 23)    // decay over ~90 minute periods
            {
                decayRate = 0.09;
            }
            else if (numPeriods > 21)    // decay over ~100 minute periods
            {
                decayRate = 0.1035;
            }
            else // decay over ~120 min periods - top limit
            {
                decayRate = 0.12;
            }


            decayRate -= 0.01;  // adjust to a less steep decay


            for (uint i = 0; i < numPeriods; ++i)
            {
                _curve[i] = Math.Pow(Math.E, -1 * decayRate * i);
            }

            return _curve;
        }

    }
}
