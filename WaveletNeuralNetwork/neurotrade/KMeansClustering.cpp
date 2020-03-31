/*
 * KMeansClustering.cpp
 *
 *  Created on: 23 Dec 2013
 *      Author: jeevw
 */


#include <unistd.h>
#include <stdlib.h>
#include <time.h>
#include <math.h>

#include "def.h"
#include "KMeansClustering.h"



KMeansClustering KMeansClustering::kMeansClusters;


void Mean::addSample( vector<float> m )
{
	if ( m.size() < _mean.size() )
	{
		throw invalid_argument("Mean::addMean() mean has fewer that required features.");
	}

	_count++;
	for ( unsigned int i=0; i< _mean.size(); ++i )
	{
		_mean_acc[i] += m[i];
		_mean[i] = _mean_acc[i] / _count;
	}
}


float Cluster::getSSE( vector<float> p1, vector<float> p2, unsigned int numItemsToCompare )
{
	float rtn = 0;

	if ( numItemsToCompare > p1.size()  || numItemsToCompare > p2.size() )
	{
		rtn = -1;
	}
	else
	{
		for ( unsigned int i=0; i< numItemsToCompare; ++i )
		{
			rtn += ( p1[i] - p2[i] ) * ( p1[i] - p2[i] );
		}
	}
	return sqrt(rtn);
}


void Cluster::setStatistics()
{
	vector<double> m( Param::InputSampleSize +1, 0.0 );

	_size = _cluster.size();

	if ( !_mean_set)
	{
		_mean = vector<float>( Param::InputSampleSize +1, 0.0 );
		for ( unsigned int i=0; i < _size; ++i )
		{
			for ( unsigned int j=0; j < Param::InputSampleSize +1; ++j )
			{
				m[j] += _cluster[i][j];
			}
		}

		for ( unsigned int j=0; j < Param::InputSampleSize +1; ++j )
		{
			_mean[j] = m[j] / _size;
		}
		_mean_set = true;
		_sse_set = false;
	}

	if ( !_sse_set)
	{
		_sse = 0;
		for ( unsigned int i=0; i < _size; ++i )
		{
			for ( unsigned int j=0; j < Param::InputSampleSize +1; ++j)
			{
				_sse += (_cluster[i][j] - m[j]) * (_cluster[i][j] - m[j]);
			}
		}
		_sse = sqrt(_sse);
		_sse_set = true;
	}
}


void KMeansClustering::clear()
{
	_clusters.clear();
	_numClusters = 0;
	_sse = 0.0;
	_sse_set = true;
	_numSamples = 0;
}

void KMeansClustering::initClustering( ClusterT &cl )
{
	Cluster cl1( cl );
	_clusters.clear();
	_clusters.push_back( cl1 );
	_numClusters = 1;
	_sse = cl1.getSSE();
	_sse_set = true;
	_numSamples = getNumSamples();
}

void KMeansClustering::addCluster( ClusterT &cl)
{
	Cluster c( cl );
	_clusters.push_back( c );

	++_numClusters;
	if ( _sse_set )
	{
		_sse += c.getSSE();
	}

	_numSamples = getNumSamples();
}

void KMeansClustering::addCluster( ClusterT &cl, Mean &m )
{
	Cluster c( cl, m.getMean() );
	_clusters.push_back( c );

	++_numClusters;
	if ( _sse_set )
	{
		_sse += c.getSSE();
	}

	_numSamples = getNumSamples();
}

unsigned int KMeansClustering::getNumSamples()
{
	list<Cluster>::iterator i;
	int s = 0;
	for ( i = _clusters.begin(); i != _clusters.end(); i++ )
	{
		s += (*i).getSize();
	}
	return s;
}

double KMeansClustering::getSSE()
{
	list<Cluster>::iterator i;
	if (!_sse_set)
	{
		_sse = 0.0;
		for ( i = _clusters.begin(); i != _clusters.end(); i++ )
		{
			_sse += (*i).getSSE();
		}
		_sse_set = true;
	}
	return _sse;
}




unsigned long KMeansClustering::bisect()
{
	unsigned int clusterToBisect = 0, count = 0, minClusterSize;
	unsigned long biggestClusterSize = 0;
	list<Cluster>::iterator cli = _clusters.begin(), i;
	list<Cluster>::iterator biggestcli = _clusters.begin();
	int cl1_seed, cl2_seed;
	//float distance, seed_distance;
	int clsize;
	KMeansClustering bisection, cl;
	ClusterT C1, C2;

	// Pick the cluster with the biggest SSE to bisect
	cout<< "Clusters: [ size, SSE ]"<< endl;
	for ( i = _clusters.begin(); i != _clusters.end(); i++, ++count )
	{
		cout<< " [ "<< i->getSize()<< ", "<< i->getSSE()<< " ] ";
		//if ( (i != cli) &&  (i->getSize() > cli->getSize() ) ) // bisect the biggest cluster in size
		if ( (i != cli) &&  (i->getSSE() > cli->getSSE() ) )	// bisect the cluster with the biggest SSE
		{
			clusterToBisect = count;
			cli = i;
			cout<< "* ";
		}
		if ( (i != biggestcli) &&  ( i->getSize() > biggestcli->getSize() ) )
		{
			biggestcli = i;
		}
	}
	cout<< endl;
	Util::log( INFO, string("[KMeansClustering::bisect()] Picked cluster ") + Util::itoa(clusterToBisect)
					 + " to bisect with SSE " + Util::ftoa( cli->getSSE() )
					 + " and size " + Util::itoa( cli->getSize() ) + "." );


	count = 0;
	clsize = cli->getSize();
	minClusterSize = clsize * 0.01;
	/* repeat bisection from here for N iterations and pick the best bisection */
	for ( int n=0; n < Param::KMeans_Bisecting_Runs; ++n )
	{
		// pick random samples as the seeds for the 2 clusters
		cl1_seed = rand() % clsize;
		do {
			cl2_seed = rand() % clsize;
		} while ( cl2_seed == cl1_seed );


	//	Util::log( INFO, string( "[KMeansClustering::bisect()] Picked samples ") + Util::itoa( cl1_seed )
	//					 + " and " + Util::itoa( cl2_seed ) + " as bisecting seeds. " );


		ClusterT cluster1( 1, cli->at( cl1_seed ) );
		ClusterT cluster2( 1, cli->at( cl2_seed ) );
		Mean mean1( cli->at( cl1_seed ) );
		Mean mean2( cli->at( cl2_seed ) );
		double dist1, dist2;

		for (int j=0; j < clsize; ++j )
		{
			if ( j == cl1_seed   ||  j == cl2_seed ) continue; // already placed

			dist1 = Cluster::getSSE( cli->at(j), mean1.getMean(), Param::InputSampleSize );
			dist2 = Cluster::getSSE( cli->at(j), mean2.getMean(), Param::InputSampleSize );

			if ( dist2 - dist1 > Util::float_zero ) // add this sample to cluster_1
			{
				cluster1.push_back( cli->at(j) );
				mean1.addSample( cli->at(j) );
			}
			else // add this sample to cluster_2
			{
				cluster2.push_back( cli->at(j) );
				mean2.addSample( cli->at(j) );
			}
		}

		// Now we have a bicsection.
		cl.clear();
		cl.addCluster( cluster1, mean1 );
		cl.addCluster( cluster2, mean2 );

		cout<< "Cluster sizes: c1 ["<< cluster1.size()<< "] + c2 ["<< cluster2.size()<< "]";
		cout<< "\t Bisection SSE: "<< cl.getSSE();

		if ( cluster1.size() < minClusterSize  ||  cluster2.size() < minClusterSize )
		{
			// We need regularization not to ive us one tiny cluster with a few samples
			// and another with millions of samples. Hence we regularize to make clusters
			// no smaller than 1% of the original sample size
			--n;
			++count;	// count the too small clustring runs

			// if the search is getting too long decrease the
			// required minimum cluster size with a slight adjustment
			if ( count % (2 * Param::KMeans_Bisecting_Runs) == 0 )
			{
				minClusterSize *= 0.99;
				cout<< "\n[KMeansClustering::bisect()] set minimum cluster size to: -- "<< minClusterSize << " --";
			}
		}
		else if ( n==0  ||  bisection.getSSE() - cl.getSSE() > Util::float_zero )
		{
			cout<< "  ***  ";

			bisection.clear();
			bisection.addCluster( cluster1, mean1 );
			bisection.addCluster( cluster2, mean2 );
			C1.assign( cluster1.begin(), cluster1.end() );
			C2.assign( cluster2.begin(), cluster2.end() );
		}
		cout<< endl;

	}

	Util::log( INFO, string("[KMeansClustering::bisect()] Previous SSE: ") + Util::ftoa( getSSE() ) );


	biggestClusterSize = ( cli == biggestcli ) ? 0 : biggestcli->getSize();
	// delete bisected cluster
	_clusters.erase( cli );
	--_numClusters;
	_sse_set = false;
	Util::log( INFO, string("[KMeansClustering::bisect()] Bisected Cluster removed. NumClusters: ") + Util::itoa(_clusters.size()) );

	// add the 2 news clusters
	cout<< "Cluster sizes: c1 ["<< bisection.getClusterAt(0)->getSize()
			<< "] + c2 ["<< bisection.getClusterAt(1)->getSize()<< "]"<< endl;
//	cout<< "Bisection SSE: "<< bisection.getSSE()<< endl;
	_clusters.push_back( *bisection.getClusterAt(0) );
	_clusters.push_back( *bisection.getClusterAt(1) );
	_numClusters += 2;
	_sse_set = false;


	if ( bisection.getClusterAt(0)->getSize() > biggestClusterSize )
	{
		biggestClusterSize = bisection.getClusterAt(0)->getSize();
	}
	if ( bisection.getClusterAt(1)->getSize() > biggestClusterSize )
	{
		biggestClusterSize = bisection.getClusterAt(1)->getSize();
	}

	Util::log( INFO, string("[KMeansClustering::bisect()] New Clusters added. Clustering size: ") + Util::itoa(_clusters.size()) );
	Util::log( INFO, string("[KMeansClustering::bisect()] New SSE: ") + Util::ftoa( getSSE() ) );
	//cout<< " **** Num Clusters : "<< _numClusters<< endl;
	//sleep(2);

	return biggestClusterSize;
}



void KMeansClustering::bisectingKMeansClustering( unsigned int k  )
{
	unsigned int biggestClusterSize = 0, i=0;

	srand( time(NULL) );
	do
	{
		biggestClusterSize = bisect();
		cout<< " **** Num Clusters : "<< _numClusters<< endl;
		Util::log( INFO, string( "[KMeansClustering::besectingKMeansClustering()] Bisecting runs: ") + Util::itoa(++i) );
		Util::log( INFO, string("[KMeansClustering::bisectingKMeansClustering()] We have clusters: ") + Util::itoa(_numClusters) );
	}
	while (  i < k-1  &&  biggestClusterSize > 0.01 * _numSamples );
}


unsigned int KMeansClustering::getKMeans( vector< vector<float> > &meansVec )
{
	list<Cluster>::iterator cli = _clusters.begin();

	for ( ; cli != _clusters.end(); cli++ )
	{
		meansVec.push_back( cli->getMean() );
	}

	return meansVec.size();
}










