/*
 * algodb.cpp
 *
 *  Created on: 7 Sep 2013
 *      Author: jeevw
 */

#include <cstdlib>
#include <dirent.h>
#include <string.h>
#include <deque>

#include "neurotrdb.h"
#include "def.h"



void Database::init()
{

	SQLRETURN res;

	// Environment Allocation
	res = SQLAllocHandle( SQL_HANDLE_ENV, SQL_NULL_HANDLE, &_env);
	if ( !SQL_SUCCEEDED(res) )
	{
		Util::log( CRITICAL, "[Database::init()] failed to allocate db environment.");
		throw DBInitFailedException();
	}

	// ODBC: Version: Set
	res = SQLSetEnvAttr( _env, SQL_ATTR_ODBC_VERSION, (void*)SQL_OV_ODBC3, 0);
	if ( !SQL_SUCCEEDED(res) )
	{
		Util::log( CRITICAL, "[Database::init()] failed to set odbc version.");
		throw DBInitFailedException();
	}

	// DBC: Allocate
	res = SQLAllocHandle( SQL_HANDLE_DBC, _env, &_dbc);
	if ( !SQL_SUCCEEDED(res) )
	{
		Util::log( CRITICAL, "[Database::init()] database allocation failed." );
		throw DBInitFailedException();
	}

	Util::log( DEBUG, "[Database::init()] Database connector initialised for "+ Param::DBName +".");

}

int Database::connect()
{
	SQLRETURN res;
	int rtn =0;

	if ( _dbc == NULL )
		init();

	// DBC: Connect
	res = SQLConnect( _dbc, (SQLCHAR*) Param::DBServerName.c_str(), SQL_NTS,
						    (SQLCHAR*) Param::DBUser.c_str(), SQL_NTS,
						    (SQLCHAR*) Param::DBPasswd.c_str(), SQL_NTS);
	if ( !SQL_SUCCEEDED(res) )
	{
		Util::log( CRITICAL, "[Database::connect()] connection to the database failed." );
		handle_errors( SQL_HANDLE_DBC, _dbc);
		throw DBInitFailedException();
	}

	_connected = true;
	Util::log( DEBUG, "[Database::connect()] Connected to the database " +Param::DBName +".");
	return rtn;
}

inline void Database::disconnect()
{
	SQLDisconnect( _dbc );
	Util::log( DEBUG, "[Database::~Database()] Disconnected from database." );
}

Database::~Database()
{
	SQLDisconnect( _dbc );
	SQLFreeHandle( SQL_HANDLE_DBC, _dbc );
	SQLFreeHandle( SQL_HANDLE_ENV, _env );
	Util::log( DEBUG, "[Database::~Database()] Disconnected from database." );
}


void Database::handle_errors( SQLSMALLINT handleType, SQLHANDLE &hndl )
{
	 SQLCHAR       sqlState[6], msg[SQL_MAX_MESSAGE_LENGTH];
	 SQLINTEGER    nativeErr;
	 SQLSMALLINT   msgLen;
	 SQLRETURN     rc = SQL_SUCCESS;

	 switch (handleType )
	 {
	   case SQL_HANDLE_ENV: rc = SQLGetDiagRec( SQL_HANDLE_ENV, hndl, 1, sqlState, &nativeErr,
			 	 	 	 	 	 	 	 	  msg, sizeof(msg), &msgLen );
	 	 	 	 	 	  	break;
	   case SQL_HANDLE_DBC: rc = SQLGetDiagRec( SQL_HANDLE_DBC, hndl, 1, sqlState, &nativeErr,
	 	 	 	  	  	  	  	  	  	  	  msg, sizeof(msg), &msgLen );
	 	 	 	 	 	    break;
	   case SQL_HANDLE_STMT: rc = SQLGetDiagRec( SQL_HANDLE_STMT, hndl, 1, sqlState, &nativeErr,
	 			 	 	 	 	 	 	 	 	  msg, sizeof(msg), &msgLen );
	 	 	 	 	 	 	break;
	   case SQL_HANDLE_DESC: rc = SQLGetDiagRec( SQL_HANDLE_DESC, hndl, 1, sqlState, &nativeErr,
	 	 	 	 	  	  	  	  	  	  	  	  msg, sizeof(msg), &msgLen );
	 	 	 	 	 	 	break;
	 }
	 if ( !SQL_SUCCEEDED(rc) )
	 {
		 Util::log( DEBUG, "[Database::handle_errors()] failed to get sqlca state. Error: " + Util::itoa(rc) );
		 return;
	 }
	 msg[msgLen] = '\0';
	 cerr<< "[Database::handle_errors()] SqlState: "<< sqlState
		 << ", NativeError: "<< nativeErr
		 << ", ErrorMsg: "<< msg<< endl;


}


//***************************************************************************************
// class AlgoDb

string NeuroTrDb::_table[] =
{
	"neurotrdb.ftse100_futures_bars_t",
	"neurotrdb.bar_size_t"
};

int NeuroTrDb::clearTable( Table t )
{
	Util::log( INFO, string("Clearing table ") + _table[t] );
	string sql("TRUNCATE TABLE " + _table[t]);
	return execSQL( sql );
}

int NeuroTrDb::execSQL( string sqlStatement )
{
	SQLHSTMT hstmt;
	bool wasConnected = _connected;
	int res, rtn = 0;

	if ( !_connected )
	{
		res = connect();
		if ( res < 0 )
		{
			Util::log( ERROR, "[NeuroTrDb::execSQL()] Could not connect to the database." );
			return -1;
		}
	}
	else
	{
		Util::log( SUCCESS, "*** db connected" );
	}

	res = SQLAllocHandle( SQL_HANDLE_STMT, _dbc, &hstmt);
	if ( !SQL_SUCCEEDED(res) )
	{
		Util::log( ERROR, "[NeuroTrDb::execSQL()] SQLAllocHandle failed.");
		cout<< "** "<< SQL_INVALID_HANDLE << endl;
		handle_errors( SQL_HANDLE_STMT, hstmt );
		rtn = -1;
		goto execSQL_get_out;
	}

	res = SQLExecDirect( hstmt,
						 (SQLCHAR *)sqlStatement.c_str(),
						 SQL_NTS );
	if ( !SQL_SUCCEEDED(res) )
	{
		Util::log( ERROR, "[NeuroTrDb::execSQL()] SQLExecDirect failed." );
		handle_errors( SQL_HANDLE_STMT, hstmt );
		rtn = -1;
	}

execSQL_get_out:

	if ( !wasConnected )
	{
		disconnect();
		Util::log( DEBUG, "[NeuroTrDb::execSQL()] Disconnected from database." );
	}

	return rtn;
}


int NeuroTrDb::loadData()
{
	// parse the data directory and read in all the data file names
	DIR *pdir = NULL;
	struct dirent *entry;
	string sql;
	int rtn;

	pdir = opendir( Param::DBDataDir.c_str() );
	if ( !pdir )
	{
		Util::log( CRITICAL, "[NeuroTrDb::loadData()] Cannot access NeuroTrade data directory: " + Param::DBDataDir );
		return -1;
	}

	clearTable( NeuroTrDb::FTSE100_FUTURES_BARS );

	while ( (entry = readdir(pdir)) )
	{
		if ( strcmp(entry->d_name, ".") != 0 && strcmp(entry->d_name, "..") != 0 )
		{
			Util::log( INFO, string(" Loading Bar data file: ") + entry->d_name );

			sql = string( "TRUNCATE TABLE ") + _table[ NeuroTrDb::FTSE100_FUTURES_BARS ] + "; ";
			execSQL( sql );

			sql = string("LOAD DATA LOCAL INFILE '") + Param::DBDataDir + "/" + entry->d_name
					+ "' INTO TABLE " + _table[ NeuroTrDb::FTSE100_FUTURES_BARS ]
					+ " FIELDS TERMINATED BY ',' "
					+ " IGNORE 1 LINES "
					+ " ( @col1, time, open, high, low, close, upticks, downticks ) "
					+ " SET `bar_size_id` = 2, "
					+ "     `date` = str_to_date( @col1, '%m/%d/%Y'), "
					+ "     `return` = 0;";
			cout<< sql << endl;

			rtn = execSQL( sql );
			if ( rtn == 0 )
			{
				Util::log( SUCCESS, string("[NeuroTrDb::loadData()] Loaded NeuroTrade data in: ") +entry->d_name);
			}
			else
			{
				Util::log( ERROR, string("[NeuroTrDb::loadData()] Failed to load NeuroTrade data in: ") +entry->d_name);
			}
		}
	}

	// now calculate the returns
	sql = "{ CALL sp_calc_returns() }";
	rtn = execSQL( sql );
	if ( !SQL_SUCCEEDED(rtn) )
	{
		Util::log( ERROR, "[NeuroTrDb::loadData()] Could not calculate returns in the database." );
		rtn = -1;
	}
	Util::log( DEBUG, "[NeuroTrDb::loadData()] Returns calculated." );

	return rtn;
}


int NeuroTrDb::readSamples( WaveletNN &wnn )
{
	SQLHSTMT hstmt;
	string sql;
	int rc, rtn = 0, i = 0;
	bool wasConnected = _connected;

	DbReturn r;
	deque<float> sample( Param::InputSampleSize, 0);

	if ( !_connected )
	{
		rc = connect();
		if ( rc < 0 )
		{
			Util::log( ERROR, "[NeuroTrDb::getReturns()] Could not connect to the database." );
			return -1;
		}
	}

	rc = SQLAllocHandle(SQL_HANDLE_STMT, _dbc, &hstmt);
	if ( !SQL_SUCCEEDED(rc) )
	{
		Util::log( ERROR, "[NeuroTrDb::getReturns()] Could not allocate statement handle." );
		handle_errors( SQL_HANDLE_STMT, hstmt );
		rtn = -1;
		goto execSQL_get_out;
	}

	sql = string("SELECT `return` FROM `neurotrdb`.`ftse100_futures_bars_t`") +
				 "WHERE `bar_id` > 1  ORDER BY `bar_id`;";
	rtn = SQLExecDirect( hstmt,
						 (SQLCHAR *)sql.c_str(),
						 SQL_NTS );
	if ( !SQL_SUCCEEDED(rtn) )
	{
		Util::log( ERROR, "[NeuroTrDb::getReturns()] Could not SELECT returns from the database." );
		rtn = -1;
	}
	Util::log( DEBUG, "[NeuroTrDb::getReturns()] Returns SELECTed." );

	rc = SQLBindCol( hstmt, 1, SQL_C_FLOAT, (SQLPOINTER)&r.price, 1, &r.ind );

	i=0;
	while ( SQL_SUCCEEDED((rc = SQLFetch( hstmt ) ) ) )
	{
		if ( i < Param::InputSampleSize +1 )  // The last (+1) is the future bar to predict
		{
			sample.push_back( r.price );
		}
		else // a full sample read in
		{
			wnn.addSample( sample );

			sample.pop_front();
			sample.push_back( r.price );
		}
		++i;
	}
	wnn.addSample( sample ); // the last one
	Util::log( DEBUG,  string("[NeuroTrDb::getReturns()] Read in ")
			+ Util::itoa(wnn.getNumSamples()) + " training samples.");

	if ( !wasConnected ) disconnect();
	return 0;


execSQL_get_out:

	if ( !wasConnected )
	{
		disconnect();
		Util::log( DEBUG, "[AlgoDb::execSQL()] Disconnected from database." );
	}

	return -1;

}








