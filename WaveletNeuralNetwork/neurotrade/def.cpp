/*
 * param.cpp
 *
 *  Created on: 7 Sep 2013
 *      Author: jeevw
 */

#include <sstream>
#include <time.h>
#include <stdexcept>
#include "def.h"

const string Param::DBName("neurotrdb");
const string Param::DBServerName( "myodbc5" ); //"neurotrade_neurotrdb");
const string Param::DBUser("neurotr");
const string Param::DBPasswd("neurotr");
const string Param::DBDataDir("/usr/local/data/neurotr/data");


string Util::_logLevel[] = { "SUCCESS", "INFO", "DEBUG", "ERROR", "CRITICAL" };

string Util::itoa( int i )
{
	std::ostringstream os;
	os << i;
	return os.str();
}

string Util::ftoa( float i )
{
	std::ostringstream os;
	os << i;
	return os.str();
}

int Util::atoi( string s )
{
	int i;
	std::stringstream ss;
	ss << s;
	try
	{
		ss >> i;
	}
	catch (exception &ex )
	{
		throw invalid_argument( "[Util::atoi()] input is not an int." );
	}
	return i;
}

float Util::atof( string s )
{
	float f;
	std::stringstream ss;
	ss << s;
	try
	{
		ss >> f;
	}
	catch (exception &ex )
	{
		throw invalid_argument( "[Util::atof()] input is not a float." );
	}
	return f;
}


string Util::getTimestamp()
{
	time_t ltime;
	struct tm result;
	char stime[64];

	ltime = time(NULL);
	localtime_r(&ltime, &result);
	strftime( stime, 64, "%Y-%m-%d %H:%M:%S", &result);

	return stime;
}


void Util::log( LogSeverity level, string msg)
{
	if ( level == SUCCESS )
	{
		clog<< "[SUCCESS : "<< getTimestamp()<< "]"<< msg<< endl;
	}
	else
	{
		cerr<< "["<< _logLevel[level]<< " : "<< getTimestamp()<< "]"<< msg<< endl;
	}
}


