%--------------------------------------------------------------------------
% Fitting 2 GMMs for Gender Recognition.
%--------------------------------------------------------------------------
home='C:\src\mos\matlab\gmm';
cd(home);
addpath(genpath(pwd));

%data_dir='C:\data\vox1_19';     % 39 features 12MFCC_0_D_A
data_dir='C:\data\vox1_19e';   % 50 features 24MFCC_E_D
male_labelf=fullfile( data_dir, 'vox1_male_id_19_data.csv');
female_labelf=fullfile( data_dir, 'vox1_female_id_19_data.csv');
f1 = fopen(male_labelf);
f2 = fopen(female_labelf);
M1 = textscan( f1, '%d %s %s', 'Delimiter',' \n');
M2 = textscan( f2, '%d %s %s', 'Delimiter',' \n' );
fclose(f1);
fclose(f2);
[r1,c] = size( M1{1} );
[r2,c] = size( M2{1} );
names_m = unique( reshape( M1{2}, 1, r1 ) );
names_f = unique( reshape( M2{2}, 1, r2 ) );
speaker_m = reshape( M1{2}, 1, r1 );
speaker_f = reshape( M2{2}, 1, r2 );
file_m = reshape( M1{3}, 1, r1 ); 
file_f = reshape( M2{3}, 1, r2 );% Read the training data.
[c,n] = size(names_m);
N = round( n/2 );     % number of speakers used for training
clear M1 M2 c male_labelf female_labelf

I = find( strcmp( speaker_m, names_m(N+1) ) );
tst_start_m = I(1);
I = find( strcmp( speaker_f, names_f(N+1) ) );
tst_start_f = I(1);
total_train_files = tst_start_m - 1 + tst_start_f - 1;

nm = 1;
nf = 1;
x = 0;
first_round = true;
for i = 1:N
    % train the model on the i'th male's speech
    fprintf( '[%u] %s\n', i, names_m{i} );
    while ( strcmp( speaker_m{ nm }, names_m{i} ) )
        datf = fullfile( data_dir, speaker_m{nm}, file_m{nm} );
        fprintf( '  [Male] Training with %s : ', datf );
        [ X, period, paramkind ] = readhtk_lite( datf );
        X = transpose( X );
        x = x+1;
        MXX{x} = X;
        [r, c] = size(X);
        MY{x} = zeros( [1, c] ); % male label is 0
        fprintf( '[ %u, %u] \n', r, c );
        nm = nm + 1;
    end
    fprintf( '\n' );
end
    
for i = 1:N
    % train the model on the i'th female's speech
    fprintf( '[%u] %s\n', i, names_f{i} );
    while ( strcmp( speaker_f{ nf }, names_f{i} ) )
        datf = fullfile( data_dir, speaker_f{nf}, file_f{nf} );
        fprintf( '  [Female] Training with %s: ', datf );
        [ X, period, paramkind ] = readhtk_lite( datf );
        X = transpose( X );
        x = x+1;
        FXX{x} = X;
        [r, c] = size(X);
        FY{x} = ones( [1, c] ); % female label is 1
        fprintf( '[ %u, %u]\n', r, c );
%         if ( first_round )
%             [y1, GMM, L] = mixGaussVb( X, 3 );
%             first_round = false;
%         else
%             [y1, GMM, L] = mixGaussVb( X, 3, GMM);
%         end
        nf = nf + 1;
    end
    fprintf( '\n' );
end
clear X;

fprintf( 'Final male data matrix: \n' );
[ y, mx ] = size(MXX);
MXXX = [ MXX{1:mx} ]';
MYYY = [ MY{1:mx} ]';
size( MXXX )

fprintf( 'Final female data matrix: \n' );
[ y, fx ] = size(FXX);
FXXX = [ FXX{1:fx} ]';
FYYY = [ FY{1:fx} ]';
size( FXXX )

XXX = [ MXX{1:mx} FXX{1:fx} ]';

clear X MX MXX FX FXX 

%--------------------------------------------------------------------------
% Independent Component Analysis to extract P primary components. 
% The rest is considered noise.
ICA_components = 10
ICA3 = rica( XXX, ICA_components, 'IterationLimit', 10000 );
MX_ICA = MXXX * ICA3.TransformWeights;
FX_ICA = FXXX * ICA3.TransformWeights;
% clear XXX

%MICA3 = rica( MXXX, 3, 'IterationLimit', 10000 );
%MX_ICA = MXXX * ICA3.TransformWeights;
%fprintf( 'Male ICA 3-component training matrix:\n');
% clear MXXX MYYY
%size(MX_ICA)

% FICA3 = rica( FXXX, 3, 'IterationLimit', 10000 );
% FX_ICA = FXXX * FICA3.TransformWeights;
% fprintf( 'Female ICA 3-component training matrix:\n');
% size(FX_ICA)
% clear FXXX FYYY 

%--------------------------------------------------------------------------
% Fit Gaussian Mixture Models with 2, 4, 8, 12, 16, 24, 32 and 64 mixtures
% generating 2 Gender Models M_GMM and F_GMM that best first the 
% gender training samples.
%mix = [ 18 24 32 36 40];
mix = [ 16 18 24 32 ];
M = length( mix );
gmm_options = statset('MaxIter',1000);
m_BIC = zeros(1,M);
best_i = 0;
best_val = 999999999999999999;
for i = 1:M
    k = mix(i);
    fprintf( 'Training male GMM for %d mixtures. . . \n', k);
    m_gmm{i} = fitgmdist( MX_ICA, k, 'RegularizationValue', 0.3, 'Options', gmm_options);
    m_BIC(i) = m_gmm{i}.BIC;
    if ( m_BIC(i) < best_val )
        best_val = m_BIC(i);
        best_i = i;
    end
end
M_GMM = m_gmm{best_i};
%clear MX_ICA
fprintf('Selected Male GMM with %d mixtures.\n\n', mix(best_i) );

clear f_gmm;
%mix = [ 18 24 32 36 40 ];
mix = [ 16 18 24 32 ];
M = length( mix );
f_BIC = zeros( 1, M);
best_i = 0;
best_val = 999999999999999999;
for i = 1:M
    k = mix(i);
    fprintf( 'Training female GMM for %d mixtures. . . \n', k);
    f_gmm{i} = fitgmdist( FX_ICA, k, 'RegularizationValue', 0.3, 'Options', gmm_options);
    f_BIC(i) = f_gmm{i}.BIC;
    if ( f_BIC(i) < best_val )
        best_val = f_BIC(i);
        best_i = i;
    end
end
F_GMM = f_gmm{best_i};
%clear FX_ICA
fprintf('Selected Female GMM with %d mixtures.\n\n', mix(best_i) );

format long g
fprintf( '\nBIC Scores:\n' );
fprintf( '                    Mixture #        Male GMM BIC             Female GMM BIC\n' );
disp( [reshape( mix, M, 1) reshape( m_BIC, M, 1 )  reshape( f_BIC, M, 1)] );
clear m_BIC f_BIC;

%--------------------------------------------------------------------------
%  Read in test samples.
[c, n] = size(names_m);
first_round = true;
fprintf( '\n----------------- Test Dataset -----------------\n');
[r, male_test_max] = size( speaker_m);
[r, female_test_max] = size( speaker_f);
nm = 1181;
N = round( n/2 );

format long g
fprintf( 'Classifying male segments...\n' );
male_seg = 0;
female_seg = 0;
undef = 0;
for i = N+1:n
    while ( (nm <= male_test_max)  &&  strcmp( speaker_m{ nm }, names_m{i} ) )
        datf = fullfile( data_dir, speaker_m{nm}, file_m{nm} );
        [ X, period, paramkind ] = readhtk_lite( datf );
        M_ICA = X * ICA3.TransformWeights;
        ym = pdf( M_GMM, M_ICA );
        F_ICA = X * ICA3.TransformWeights;
        yf = pdf( F_GMM, F_ICA );
        bhatt_dist_m = sum( sqrt( ym ) );
        bhatt_dist_f = sum( sqrt( yf ) );
        %fprintf( '[M][%s] => ', datf );
        if ( bhatt_dist_m > bhatt_dist_f )
            %    fprintf( 'M\n');
            male_seg = male_seg + 1;
        else
            if ( bhatt_dist_m < bhatt_dist_f )
                fprintf('%s => F\n', datf);
                female_seg = female_seg + 1;
            else
                fprintf( 'X\n');
                undef = undef+ 1;
            end
        end
        nm = nm + 1;
    end
end
correct = male_seg;
total = male_seg + female_seg + undef;
fprintf( '\nMale classified segments  : %d\n', male_seg );
fprintf( 'Female classified segments: %d\n', female_seg );
fprintf( 'Undefined                 : %d\n', undef );
fprintf( 'Total                     : %d\n', total );

% female
fprintf( '\nClassifying female segments...\n' );
male_seg = 0;
female_seg = 0;
undef = 0;
nf = 826;
for i = N+1:n
    while ( (nf <= female_test_max)  &&  strcmp( speaker_f{ nf }, names_f{i} ) )
        datf = fullfile( data_dir, speaker_f{nf}, file_f{nf} );
        [ X, period, paramkind ] = readhtk_lite( datf );
        M_ICA = X * ICA3.TransformWeights;
        ym = pdf( M_GMM, M_ICA );
        F_ICA = X * ICA3.TransformWeights;
        yf = pdf( F_GMM, F_ICA );
        bhatt_dist_m = sum( sqrt( ym ) );
        bhatt_dist_f = sum( sqrt( yf ) );
        %fprintf( '[F][%s] => ', datf );
        if ( bhatt_dist_m > bhatt_dist_f )
            fprintf( '%s => M\n', datf );
            male_seg = male_seg + 1;
        else
            if ( bhatt_dist_m < bhatt_dist_f )
                %        fprintf( 'F\n');
                female_seg = female_seg + 1;
            else
                fprintf( 'X\n');
                undef = undef+ 1;
            end
        end
        nf = nf + 1;
    end
end
%clear M_ICA F_ICA m_gmm f_gmm

c = correct + female_seg;
t = total + male_seg + female_seg + undef;
accuracy = ( 100 * c ) / t;

fprintf( '\nMale classified segments  : %d\n', male_seg );
fprintf( 'Female classified segments: %d\n', female_seg );
fprintf( 'Undefined                 : %d\n', undef );
fprintf( 'Total                     : %d\n', male_seg + female_seg + undef );

fprintf( '\nClassification accuracy on VoxCeleb1: %f\n', accuracy );


%---------------------------------------------------------------------------------
% Evaluate Prudential segments using Bhattacharyya distance to each gender model.

plabel_file = 'C:\src\mos\data\prudential1_segments_gender.txt';
pdata_dir = 'C:\data\prudential_enc\mfcc24';

pf = fopen(plabel_file);
PSegs = textscan( f1, '%s %d', 'Delimiter',' \n');
fclose(pf);
Segs = PSegs{1};
Gender = PSegs{2};
[ num_segs c ] = size( Segs );
clear PSegs;
M = 0;
F = 1;


% First we need to build an ICA transform correct for the Prudential
% domain.
% clear X XX XXX;
% for i = 1:num_segs
%     datf = fullfile( pdata_dir,Segs{i} );
%     [ X, period, paramkind ] = readhtk_lite( datf );
%     XX{i} = X';
% end
% XXX = [ XX{1:num_segs} ]';
% fprintf( 'Projecting Prudential data to ICA-3 feature space. . .\n' );
% PICA3 = rica( XXX, ICA_components, 'IterationLimit', 10000 );

% Evaluate test segments --------------------------------------------------
male_seg = 0;
female_seg = 0;
undef = 0;
correct = 0;
clear male_segments female_segments undefined
for i = 1: num_segs
    datf = fullfile( pdata_dir, Segs{i} );
    [ X, period, paramkind ] = readhtk_lite( datf );
    %XICA = X * PICA3.TransformWeights;
    XICA = X * ICA3.TransformWeights;
    bhatt_dist_m = bhatt_coefficient( M_GMM, XICA );
    bhatt_dist_f = bhatt_coefficient( F_GMM, XICA );
    if ( bhatt_dist_m > bhatt_dist_f )
        %fprintf( datf + ' => M\n');
        male_seg = male_seg + 1;
        male_segments{male_seg} = Segs{i};
        gender = M;
    else
        if ( bhatt_dist_m < bhatt_dist_f )
    %        fprintf( 'F\n');
            female_seg = female_seg + 1;
            female_segments{female_seg} = Segs{i};
            gender = F;
        else
            fprintf( 'X\n');
            undef = undef+ 1;
            undefined{undef} = Segs{i};
        end
    end
    
    if ( gender == Gender(i) )
        correct = correct + 1;
    else
        if ( gender == 0 )
            g = 'M';
        else
            g = 'F';
        end
        fprintf( '[%s] %s\n', g, Segs{i} );
    end
end

accuracy = ( correct * 100 )/ num_segs;
fprintf( '\nClassification accuracy on Prudential: %f\n', accuracy );

best_gmm = 2;

clear FXXX FY FYYY M_ICA F_ICA FX_ICA MX_ICA FYYY MYYY;