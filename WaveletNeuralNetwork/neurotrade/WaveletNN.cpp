/*
 * WaveletNN.cpp
 *
 *  Created on: 13 Dec 2013
 *      Author: jeevw
 */

#include "WaveletNN.h"
#include <float.h>
#include <math.h>
#include <stdexcept>
#include <set>



void WaveletNN::initClustersTestData( KMeansClustering &kMeansClusters, bool useAllTrainingData )
{
	ClusterT cluster;

	_usedAllTrainingData = useAllTrainingData;

	cout<< "Total Samples: " << _samples.size()<< endl;

	if ( useAllTrainingData )
	{
		cluster.assign( _samples.begin(), _samples.end() );
	}
	else // separate 5% as test data
	{
		for ( unsigned int i = 0; i < _samples.size(); ++i )
		{
			if ( i%10 == 0 )
			{
				_testData.push_back( _samples[i] );
			}
			else
			{
				cluster.push_back( _samples[i] );
			}
		}

		cout<< "Done separating test data"<< endl;
		_samples.clear();
		_samples = cluster;
		cout<< "Done setting training data"<< endl;

	}

	kMeansClusters.initClustering( cluster );

	cout<< "Training Samples: " << getNumSamples()
	    << " Test Samples: " << getNumTestData()
	    << "\nClustering Samples: " << kMeansClusters.getNumSamples() << endl;
	cout<< "Clustering Init SSE: "<< kMeansClusters.getSSE()<< endl;
	Util::log( INFO, "[main()] Clusters and test data initialized.\n");

	Util::log( SUCCESS, "[WaveletNN::initClustersTestData()] Done initializing KMeans Clustering" );

}



float WaveletNN::mexicanHatWavelet( vector<float> sample, vector<float> mean, float radius )
{
	float A, T2 = 0, r_2, t;
	//vector<float> t( Param::InputSampleSize, 0.0 );

	if ( radius == 0 )
	{
		throw invalid_argument("[WaveletNN::mexicanHatWavelet(] error: zero radius" );
	}

	r_2 = 1/( radius * radius);

	A = 2 / ( pow( M_PI, 0.25) * sqrt(3) * sqrt(radius) );

	for ( unsigned int i=0; i < Param::InputSampleSize; ++i )
	{
		T2 += (sample[i] - mean[i]) * (sample[i] - mean[i]);
	}

	t = A * ( 1 - T2 * r_2 ) * exp( -T2 * r_2 / 2 );

	return t;
}


void WaveletNN::setWaveletMeansAndRadius( unsigned int k, float rFactor )
{
	unsigned int K, m;
	KMeansClustering kMeansClustering;

	Util::log( INFO, "[WaveletNN::setWaveletMeansAndRadius()] Preparing clusters and test data.");
	initClustersTestData( kMeansClustering, false );

	K = ( k == 0) ? 0.60 * Param::InputSampleSize : k;
	Util::log( INFO, string("[WaveletNN::setWaveletMeansAndRadius()] Training for K = ")
					 + Util::itoa(K) + " means." );

	kMeansClustering.bisectingKMeansClustering( K );
	Util::log( INFO, "[WaveletNN::setWaveletMeansAndRadius()] Bisection K-Means Clustering completed. ");

	cout<< "\nCLUSTER SIZES: "<< endl;
	for ( unsigned int i = 0; i < kMeansClustering.getNumClusters(); ++i )
	{
		cout<< "["<< kMeansClustering.getClusterAt(i)->getSize()<<"],  ";

	}
	cout<< endl;

	m = kMeansClustering.getKMeans( _kMeans );
	Util::log( INFO, string("[WaveletNN::setWaveletMeansAndRadius()] Set ")
								+ Util::itoa(m) + " means." );

	if ( m == 1 )
	{
		throw domain_error("[WaveletNN::setWaveletMeansAndRadius()] Only 1 mean; cannot set radius.");
	}

	Util::log( INFO, "[WaveletNN::setWaveletMeansAndRadius()] Setting the Wavelet Radius.");

	list< vector<float> > meansVec( _kMeans.begin(), _kMeans.end() );
	list< vector<float> >::iterator i = meansVec.begin(), j;
	float dist = FLT_MAX, dist1 = 0;
	set<float> distances;

	_radius = 0;
	j = meansVec.begin();
	while ( meansVec.size() > 1 )
	{
		vector<float> mean1(*j);
		meansVec.erase(j);

		for ( i = meansVec.begin(); i != meansVec.end(); i++ )
		{
			dist1 = Cluster::getSSE( mean1, *i, Param::InputSampleSize );
			if ( dist1 < dist )
			{
				dist = dist1;
				j = i;
			}
		}

		distances.insert( dist );
		_radius += dist;
		dist = FLT_MAX;
	}
	_radius = rFactor *  _radius / ( _kMeans.size() -1);

	Util::log( INFO, string("[WaveletNN::setWaveletMeansAndRadius()] Wavelet radius with the mean: ")
			+ Util::ftoa( _radius ) );

/*-------------------------------------------
 *  Setting the radius with the median distance

	set<float>::iterator p;
	unsigned int count, pick;

	count = 0;
	pick = round( ( distances.size() + 0.3)/2 );
	cout<< "Distances between means: "<< endl<< "[ ";
	for ( p = distances.begin(); p!= distances.end(); p++ )
	{
		if ( ++count ==  pick )
		{
			_radius = *p;
			cout<< "*";
		}
		cout<< *p << ", ";
	}
	cout<< " ], Picking distance id "<< pick << endl;

	Util::log( INFO, string("[WaveletNN::setWaveletMeansAndRadius()] Wavelet radius with the meadian: ")
				+ Util::ftoa( _radius ) );
*/

}



void WaveletNN::trainWeights()
{
	unsigned int i, j;
	leda::matrix hiddenLayerM;
	leda::vector Y;

	hiddenLayerM 	= leda::matrix( _samples.size(), _kMeans.size() + 1 );
	_weights 		= leda::vector( _kMeans.size() + 1 );
	Y				= leda::vector( _samples.size() );


	for ( i = 0; i < _samples.size(); ++i )
	{
		hiddenLayerM( i, 0 ) = 1.0;
		for ( j = 1; j < _kMeans.size() + 1; ++j )
		{
			hiddenLayerM( i, j ) = WaveletNN::mexicanHatWavelet( _samples[i], _kMeans[j-1], _radius );
		}
		Y[i] = _samples[i][Param::InputSampleSize];
	}

	Util::log( INFO, "[WaveletNN::trainWeights()] Hidden layer set.");

	leda::matrix Mtrans = hiddenLayerM.trans();
	Util::log( INFO, "[WaveletNN::trainWeights()] M_trans estimated.");

	leda::matrix M 		= Mtrans * hiddenLayerM ;
	Util::log( INFO, "[WaveletNN::trainWeights()] M_inv step one done.");

	leda::matrix Minv	= M.inv() * Mtrans;
	Util::log( INFO, "[WaveletNN::trainWeights()] M_inv done.");

	_weights = Minv * Y;

	Util::log( INFO, "[WaveletNN::trainWeights()] Weights trained.");

	cout<< "Weights : "<< endl;
	_weights.print();

}



float WaveletNN::predict( vector<float> sample )
{
	leda::vector h( _kMeans.size() + 1 );
	float y;

	if ( sample.size() < Param::InputSampleSize + Param::NumPredBars )
	{
		throw length_error( string("Sample length [") + Util::itoa( sample.size() )
							+ "] must be :" + Util::itoa( Param::InputSampleSize ) + "." );
	}
	vector<float> s( sample.begin(), sample.begin() + Param::InputSampleSize );


	// project to K-wavelet feature space
	h[0] = 1.0;
	for ( unsigned int i = 1; i <= _kMeans.size(); ++i )
	{
		h[i] = WaveletNN::mexicanHatWavelet( s, _kMeans[i], _radius );
	}

	y = h * _weights;
	cout<< " f(x) = "<< y << " ; "<< " y = "<< sample[ Param::InputSampleSize ]<< endl;


	return y;
}

WaveletNN::Error WaveletNN::getError( leda::vector fx, vector< vector<float> > y )
{
	Error er;
	unsigned int direrr =0;
	//float abserr, fracerr;
	//double mse = 0.0, mfe = 0.0;

	for ( unsigned int i = 0; i < y.size(); ++i )
	{
		//cout<< "y = "<< y[i][0];
		//cout<< " fx = "<< fx[1];

		// directional error
		if (  ( y[i][0] < 0  &&  fx[1] > 0 ) || ( y[i][0] > 0  &&  fx[1] < 0 ) )
		{
			// directional error
			++direrr;
			//cout << " * ";
		}
		else
		{
			//cout << "  ";
		}
		//cout<< ",\t";
	}
	cout<< endl<< endl;

	er.directional_err = 100 * direrr / y.size();

	er.mean_sqr_err = 0.0;
	er.mean_fractional_error = 0.0;

	cout<< "From Test Samples, directional error count : "<< direrr << " / "<< y.size()<< endl;

	cout<< "\nDirectional Accuracy: "<< 100 - er.directional_err<< "%" << endl;

	return er;
}


leda::vector WaveletNN::predict( vector< vector<float> > samples ) const
{
	leda::matrix M( samples.size(), _kMeans.size() + 1 );
	leda::vector fx;
	vector< vector<float> > y;

	cout<< "done"<< endl;

	for ( unsigned int i = 0; i < samples.size(); ++i )
	{
		if ( samples[i].size() < Param::InputSampleSize + Param::NumPredBars )
		{
			Util::log( ERROR, string("Sample length [") + Util::itoa( samples[i].size() )
								+ "] must be :" + Util::itoa( Param::InputSampleSize ) + "." );
			continue;
		}

		vector<float> yy(1);
		yy[0] = samples[i][Param::InputSampleSize];
		y.push_back( yy );


		M( i, 0 ) = 1.0;
		for ( unsigned int j = 0; j < _kMeans.size(); ++j )
		{
			M( i, j+1) = WaveletNN::mexicanHatWavelet( samples[i], _kMeans[j], _radius );
		}
	}

	cout<< "done1"<< endl;

	fx = M * _weights;

	cout<< "\n\nGetting error"<< endl;

	WaveletNN::getError( fx, y );

	return fx;
}


