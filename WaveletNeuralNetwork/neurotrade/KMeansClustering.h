/*
 * KMeansClustering.h
 *
 *  Created on: 23 Dec 2013
 *      Author: jeevw
 */

#ifndef _NEUROTRADE_KMEANSCLUSTERING_H_
#define _NEUROTRADE_KMEANSCLUSTERING_H_

#include <vector>
#include <list>
#include <stdexcept>


using namespace std;


typedef vector< vector<float> > ClusterT;


class Mean
{
public:


	Mean( unsigned int size )
		: _count(0), _mean_acc( size, 0.0 ), _mean( size, 0.0 )
	{}

	Mean( vector<float> m )
		: _count(1), _mean_acc( m.begin(), m.end()), _mean( m.begin(), m.end() )
	{}

	void addSample( vector<float> m );
	vector<float> getMean() const { return _mean; }

private:

	unsigned long	_count;
	vector<double>  _mean_acc;
	vector<float> 	_mean;

};


class Cluster
{

public:

	static float getSSE( vector<float> p1, vector<float> p2, unsigned int numItemsToCompare );


	Cluster( ClusterT cl )
		: _cluster(cl), _mean_set(false), _sse_set(false)
	{ setStatistics(); }

	Cluster( ClusterT &cl, vector<float> m )
		: _cluster(cl), _mean( m.begin(), m.end() ), _mean_set(true), _sse_set(false)
	{ setStatistics(); }

	vector<float> at( unsigned int i )
	{
		_size = _cluster.size();
		if ( i >= _size )
		{
			throw out_of_range("[Cluster::at()] Index out of range.");
		}
		return _cluster.at(i);
	}

	unsigned int getSize()
	{
		_size = _cluster.size();
		return _size;
	}

	float getSSE()
	{
		if ( !_sse_set ) setStatistics();
		return _sse;
	}

	vector<float> getMean()
	{
		if ( !_mean_set )
		{
			cout<< "** Setting Mean **";
			setStatistics();
		}
		return _mean;
	}


private:

	ClusterT	_cluster;

	unsigned int 	_size;	// size of cluster

	vector<float>	_mean;	// mean of the clusters
	bool			_mean_set;

	double			_sse;	// sum-of-squared error
	bool			_sse_set;


	void setStatistics();

};



class KMeansClustering
{

  public:

	static KMeansClustering kMeansClusters;

	KMeansClustering()
	:_numClusters(0), _sse(0.0), _sse_set(true)
	{}

	unsigned int getNumSamples();
	unsigned int getNumClusters() const { return _clusters.size(); }

	void clear();
	void initClustering( ClusterT &cl );

	void addCluster( ClusterT &cl );
	void addCluster( ClusterT &cl, Mean &m );

	void addCluster( Cluster &cl )
	{
		_clusters.push_back( cl );
		++_numClusters;
		_sse_set = false;
	}

	Cluster *getClusterAt( unsigned int n )
	{
		list<Cluster>::iterator i = _clusters.begin();
		unsigned int x = 0;

		if ( n > _clusters.size() )
		{
			throw out_of_range("[KMeansClustering::getClusterAt()] Index out of range.");
		}

		for ( ; x != n; ++x, i++ );

		return &(*i);
	}

	double getSSE();


	void bisectingKMeansClustering( unsigned int k );
	unsigned int getKMeans( vector< vector<float> > &meansVec );


  private:

	list<Cluster> 	_clusters;

	unsigned long	_numSamples;
	unsigned int	_numClusters;
	double			_sse;
	bool			_sse_set;


  public:

	/* Finds the cluster with the biggest SSE.
	 * Runs N iterations of bisections.
	 * Deletes the bisected cluster.
	 * Adds the best bisection to the end.
	 * Sets the new SSE of the clustering.
	 * Returns the new SSE of the clustering.
	 */
	inline unsigned long bisect();

};

#endif /* _NEUROTRADE_KMEANSCLUSTERING_H_ */
