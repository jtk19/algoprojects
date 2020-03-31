/*
 * param.h
 *
 *  Created on: 7 Sep 2013
 *      Author: jeevw
 */

#ifndef _NEUROTRADE_DEF_H_
#define _NEUROTRADE_DEF_H_

#include <iostream>


using namespace std;


class Param {
public:

	  static const string DBName;
	  static const string DBServerName;
	  static const string DBUser;
	  static const string DBPasswd;
	  static const string DBDataDir;

	  static const int InputSampleSize = 80;

	  static const int KMeans_Bisecting_Runs = 12;

	  static const int Radius_Factor = 2.5;

	  static const int NumPredBars = 1;

};


enum LogSeverity
{
    SUCCESS,
    INFO,
    DEBUG,
    ERROR,
    CRITICAL
};

class Util
{
  public:

	static const float float_zero = 0.00001;

	static string itoa( int i );
	static string ftoa( float i );
	static string getTimestamp();

	static int	 atoi( string s );
	static float atof( string s );

	static void log( LogSeverity level, string msg);

  private:

	static string _logLevel[];

};

#endif /* _NEUROTRADE_DEF_H_ */
