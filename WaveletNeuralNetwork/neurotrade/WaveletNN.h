/*
 * WaveletNN.h
 *  The Inout Layer of the Wavelet Neural Network
 *  Created on: 13 Dec 2013
 *      Author: jeevw
 */
#ifndef _NEUROTRADE_NNINPUTLAYER_H_
#define _NEUROTRADE_NNINPUTLAYER_H_

#include <vector>
#include <deque>
#include <list>
#include <LEDA/numbers/matrix.h>
#include <LEDA/numbers/vector.h>

#include "def.h"
#include "KMeansClustering.h"


using namespace std;


class WaveletNN
{

  public:

	struct Error
	{
		float directional_err;
		float mean_sqr_err;
		float mean_fractional_error;
	};

	static WaveletNN::Error getError( leda::vector fx, vector< vector<float> > y );


  public:

	WaveletNN() : _usedAllTrainingData( false ) {};

	void addSample( deque<float> s )
		{
			vector<float> s1( s.begin(), s.end() );
			_samples.push_back( s1 );
		}
	void addSample( vector<float> s )
		{ _samples.push_back(s); }

	unsigned int getNumSamples() const { return _samples.size(); }
	unsigned int getNumTestData() const { return _testData.size(); }

	void initClustersTestData( KMeansClustering &kMeansClusters, bool useAllTrainingData );


	static float mexicanHatWavelet( vector<float> sample, vector<float> mean, float radius );

	// K is the number of means
	void setWaveletMeansAndRadius( unsigned int k, float f );

	void trainWeights();

	float predict( vector<float> sample );
	leda::vector predict( vector< vector<float> > samples ) const;

	void test() const { predict( _testData ); }


  private:

	leda::vector _weights;

	vector< vector<float> > _kMeans;
	float					_radius;

	vector< vector<float> > _samples;
	vector< vector<float> > _testData;

	bool	_usedAllTrainingData;

};

#endif /* _NEUROTRADE_NNINPUTLAYER_H_ */
