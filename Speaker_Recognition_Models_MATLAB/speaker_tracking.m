%--------------------------------------------------------------------------
% Speaker Identification and Tracking
%--------------------------------------------------------------------------
homed = 'C:\src\mos\matlab\gmm';
cd(homed);
addpath(genpath(pwd));

pdata_dir = 'C:\data\prudential_enc\mfcc24f';
cd(pdata_dir);
pspeaker_flist = dir( '*EXACT.mfcc' );
pall_flist = dir( '*.mfcc' );
cd( homed );

[ y, c] = size(pspeaker_flist);
spfiles = string( zeros(1, y) );
for i = 1:y
    spfiles(i) = pspeaker_flist(i).name;
end
clear pspeaker_flist;

[ y, c] = size(pall_flist);
u = 0;
clear XXA;
for i = 1:y
    datf = fullfile( pdata_dir, pall_flist(i).name );
    [ X, period, paramkind ] = readhtk_lite( datf );
    % delete silence rows at the end 
    del = sum( X( :, 1:10), 2 ) == 0;
    X( del, : ) = [];
    % --
    XXA{i} = transpose(X);
    if ~contains( pall_flist(i).name, 'EXACT.mfcc' )
        u = u + 1;
        unknown_files{u} = pall_flist(i).name;
    end
end
XXAll = [ XXA{1:y} ]';
clear X XXA;

%--------------------------------------------------------------------------
% Create the ICA transform from all the data
ICA_components = 10;
ICA = rica( XXAll, ICA_components, 'IterationLimit', 10000 );
XAll_ICA = XXAll * ICA.TransformWeights;
clear XXAll;

%--------------------------------------------------------------------------
% Create the Prior Speaker Independent model using all data from all speakers.
mix = [ 18 24 32 36 40 48 ];
M = length( mix );
gmm_options = statset('MaxIter', 10000);
clear si_gmm;
si_BIC = zeros( 1, M);
best_i = 0;
best_val = 999999999999999999;
clear si_gmm;
for i = 1:M
    k = mix(i);
    fprintf( 'Training Speaker Independent GMM for %d mixtures. . . \n', k);
    si_gmm{i} = fitgmdist( XAll_ICA, k, 'CovarianceType','diagonal', ...
                            'RegularizationValue', 0.1, ...
                            'Options', gmm_options);
    si_BIC(i) = si_gmm{i}.BIC;
    if ( si_BIC(i) < best_val )
        best_val = si_BIC(i);
        best_i = i;
    end
end
SI_GMM = si_gmm{best_i};

% for testing
SI_GMM = si_gmm{1};


format long g
fprintf( '\nSelected [%d] with %d mixtures. BIC Scores of SI models trained:\n', best_i, mix(best_i) );
fprintf( '                    Mixture #        GMM BIC\n' );
disp( [reshape( mix, M, 1) reshape( si_BIC, M, 1 )] );
% clear si_gmm si_BIC;

% Important take-forwards at this point: ICA transform, 
% XAll_ICA, samples transformd to ICA 10 dimenstions
% and SI_GMM, the Speaker Independent model for Speaker Adaptation.


%--------------------------------------------------------------------------
% Constrained MLLR Adaptation: Process speaker labels
data_dir='C:\data';   % 50 features 24MFCC_E_D
mllr_labelf=fullfile( data_dir, 'prudential_mllr_speakers.txt');
f1 = fopen(mllr_labelf);
M1 = textscan( f1, '%s %s', 'Delimiter',' \t\n');
fclose(f1);
[r1,c] = size( M1{1} );
segments = reshape( M1{1}, 1, r1 );
speakers = reshape( M1{2}, 1, r1 );
spid = unique( speakers );

K = ICA_components;
[ r numseg ] = size(speakers);
prevsp = string(speakers{1});
segs = 0;
mllr_count = 0;
clear W;
for i = 1:numseg 
    % For each known speaker, build a speaker adaptation transform
    if ( string(speakers{i}) ~= prevsp )
        % Fit MLLR transform
        XXX = XX{1:segs};
        XXX = transpose( XXX );
        O = XXX * ICA.TransformWeights;
        mllr_count = mllr_count + 1;
        W{ mllr_count } = mllr_transform( SI_GMM, O );
        segs = 0;
        clear X XX XXX;
        prevsp = string(speakers{i});
        %break;
    end
    datf = fullfile( pdata_dir, segments{i} );
    [ X, period, paramkind ] = readhtk_lite( datf );
    % delete silence rows at the end 
    del = sum( X( :, 1:K), 2 ) == 0;
    X( del, : ) = [];
    % --
    segs = segs + 1;
    XX{segs} = transpose(X);
end
% Final model
XXX = XX{1:segs};
XXX = transpose( XXX );
O = XXX * ICA.TransformWeights;
mllr_count = mllr_count + 1;
W{ mllr_count } = mllr_transform( SI_GMM, O );
clear O X XX XXX;

% Adapt the speaker model

clear S_GMM A A_T E;
for i = 1:mllr_count
    b = W{i}( :, 1 );
    A = W{i}( :, 2:K+1 );
    A_T = transpose(A);
    D = SI_GMM.NumVariables;
    K = SI_GMM.NumComponents;
    E = [ ones( K, 1) SI_GMM.mu ];
    clear m mu;
    %Sigma = zeros( D, D, K );
    for k = 1:K
        m{k} =  W{i} * transpose( E( k, : ) );
        %Sigma( :, :, k) = A * diag( SI_GMM.Sigma( 1, :, k ) ) * A_T;
    end
    mu = transpose( [ m{1:K} ] );
    p = SI_GMM.ComponentProportion;
    S_GMM = gmdistribution( mu, SI_GMM.Sigma, p);
    
    %     A_inv = inv(A);
    %     A_norm = norm(A);
    %     O = XAll_ICA - transpose(b);
    %     O = A_inv * transpose(O);
    %     O = transpose(O);
    %     O = O / A_norm;
    
end

mix = [ 16 18 24 ];
M = length( mix );
gmm_options = statset('MaxIter', 10000);
s_BIC = zeros( 1, M);
best_s = 0;
best_val = 999999999999999999;
clear s_gmm;
for i = 1:M
    k = mix(i);
    fprintf( 'Training Speaker Adapted GMM for %d mixtures. . . \n', k);
    s_gmm{i} = fitgmdist( O, k, 'CovarianceType','diagonal', ...
                            'RegularizationValue', 0.1, ...
                            'Options', gmm_options);
    s_BIC(i) = s_gmm{i}.BIC;
    if ( s_BIC(i) < best_s )
        best_val = s_BIC(i);
        best_s = i;
    end
end
S_GMM = s_gmm{best_s};

format long g
M = 3;
fprintf( '\nSelected [%d] with %d mixtures. BIC Scores of SI models trained:\n', best_i, mix(best_i) );
fprintf( '                    Mixture #        GMM BIC\n' );
disp( [reshape( mix, M, 1) reshape( s_BIC, M, 1 )] );

%--------------------------------------------------------------------------
% Evaluate segment distances to Speaker models
format long g
[ y, c] = size(pall_flist);
for i = 1:y
   %fprintf( 'Speaker Model for %s, Bhattacharyya coefficient of segments:\n', uuid{i} ); 
    datf = fullfile( pdata_dir, pall_flist(i).name );
    [ X, period, paramkind ] = readhtk_lite( datf );
    % delete silence rows at the end 
    del = sum( X( :, 1:10), 2 ) == 0;
    X( del, : ) = [];
    % --
    XICA = X * ICA.TransformWeights;
    bhatt_coeff = bhatt_coefficient( S_GMM, XICA );
    if ( ( bhatt_coeff > 0.000099 ) || ...
         ( string(speakers{1}) == string( getUserId(pall_flist(i).name)) ) )
        fprintf( '%f %s\n', bhatt_coeff, pall_flist(i).name );
    end
end


% save( 'C:\src\mos\prud_speaker_models.mat' );
