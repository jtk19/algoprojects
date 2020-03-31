/*
 * algodb.h
 *
 *  Created on: 7 Sep 2013
 *      Author: jeevw
 */

#ifndef _NEUROTRADE_NEUROTRDB_H_
#define _NEUROTRADE_NEUROTRDB_H_

#include <iostream>
#include <sql.h>
#include <sqlext.h>
#include <exception>

#include "WaveletNN.h"


using namespace std;


class Database {

	class DBInitFailedException: public std::exception
	{
		const char *what() const throw()
		{
			return "Failed to initialise the database";
		}
	};

public:

	Database(): _dbc(NULL), _env(NULL), _connected(false) {}
	virtual ~Database();

	void init();
	int connect();
	void disconnect();

protected:

	SQLHDBC	_dbc;
	SQLHENV	_env;

	bool _connected;

	void handle_errors( SQLSMALLINT handleType, SQLHANDLE &hndl );

};


class NeuroTrDb: public Database
{
  public:

	enum Table	{ FTSE100_FUTURES_BARS, BAR_SIZE	};

	int execSQL( string sqlStatement );
	int clearTable( Table t );
	int loadData();

	int readSamples( WaveletNN &wnn );

	struct DbReturn
	{
		float 	price;
		long 	ind;
	};

  private:

	static string _table[];

};


#endif /* _NEUROTRADE_NEUROTRDB_H_ */

