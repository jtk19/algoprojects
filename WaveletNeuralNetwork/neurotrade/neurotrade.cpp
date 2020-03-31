//============================================================================
// Name        : algoweb.cpp
// Author      : Jeevani
// Version     :
// Copyright   : Your copyright notice
// Description : Hello World in C, Ansi-style
//============================================================================

#include <iostream>

#include "def.h"
#include "neurotrdb.h"

#include "WaveletNN.h"
#include "KMeansClustering.h"


using namespace std;



int main(int argc, char **argv)
{
    Param param;
    NeuroTrDb db;
    string msg;

    WaveletNN wnn;

    int KMeans = 0;
    float RFactor = Param::Radius_Factor;

    if ( argc > 1 )
    {
    	KMeans = Util::atoi( argv[1] );
    }
    if ( argc > 2 )
    {
    	RFactor = Util::atof( argv[2] );
    	if ( RFactor > 6.0 ||  RFactor < 0.5 )
    	{
    		RFactor = Param::Radius_Factor;
    	}
    }

    cout << "Hello. Welcome to NeuroTrade." << std::endl;

    msg = string("[main()] Connecting to database: ") + param.DBName;
    Util::log( INFO, msg );
    db.connect();
    Util::log( INFO, "[main()] DB connected. \n");

    Util::log( INFO, "[main()] Loading Data.");
    db.loadData();
    Util::log( INFO, "[main()] Data loaded. \n");

    Util::log( INFO, "[main()] Reading in samples from the database.");
    db.readSamples( wnn );
    Util::log( INFO, "[main()] Wavelet NN Input Layer Ready.\n");

    /*
    Util::log( INFO, "Preparing clusters and test data.");
    wnn.initClustersTestData( KMeansClustering::kMeansClusters, false );
    cout<< "Training Samples: " << wnn.getNumSamples()
    		<< " Test Samples: " << wnn.getNumTestData()
    		<< "\nClustering Samples: " << KMeansClustering::kMeansClusters.getNumSamples() << endl;
    cout<< "Clustering Init SSE: "<< KMeansClustering::kMeansClusters.getSSE()<< endl;
    Util::log( INFO, "[main()] Clusters and test data initialized.\n");
	*/

    cout<< endl;
    Util::log( INFO, "[main()] Setting Wavelet Means and Radius with K Means Clustering.");
    wnn.setWaveletMeansAndRadius( KMeans, RFactor );
    Util::log( SUCCESS, "[main()] Wavelet Means and Radius set with K Means Clustering.");


    cout<< endl;
    wnn.trainWeights();
    Util::log( SUCCESS, "[main()] Wavelet NN weights trained.");
    Util::log( SUCCESS, "[main()] Wavelet NN trained.");


    cout<< "\nTesting" << endl;
    wnn.test();


    cout << endl<< "\nNeurotrade exiting." << endl;
    return 0;
}






