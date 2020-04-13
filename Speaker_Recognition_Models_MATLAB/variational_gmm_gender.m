% This is the Variational Bayesian Inference method for training a 
% Gaussian Mixture Model. Here we do it for Gender Detection with
% the MFCC featureset of the Oxford VoxCeleb 1 dataset.
cd 'C:\src\mos\matlab\gmm';
addpath(genpath(pwd));
addpath(genpath('..\PRMLT'));

%data_dir='C:\data\vox1_19'; % for 12mfcc_0_D_A
data_dir='C:\data\vox1_19e';   % for 24mffc_E_D
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
file_f = reshape( M2{3}, 1, r2 );
clear M1 M2 c male_labelf female_labelf
 
% Read the training data.
[c,n] = size(names_m);
N = round( n/2 );     % number of speakers used for training

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
        XX{x} = X;
        [r, c] = size(X);
        Y{x} = zeros( [1, c] ); % male label is 0
        fprintf( '[ %u, %u] \n', r, c );
        %if ( first_round )
        %    [y1, GMM, L] = mixGaussVb( X, 3 );
        %    GMM
        %    first_round = false;
        %else
        %    [y1, GMM, L] = mixGaussVb( X, 3, GMM);
        %end
        nm = nm + 1;
    end
    fprintf( '\n' );
    
    % train the model on the i'th female's speech
    fprintf( '[%u] %s\n', i, names_f{i} );
    while ( strcmp( speaker_f{ nf }, names_f{i} ) )
        datf = fullfile( data_dir, speaker_f{nf}, file_f{nf} );
        fprintf( '  [Female] Training with %s: ', datf );
        [ X, period, paramkind ] = readhtk_lite( datf );
        X = transpose( X );
        x = x+1;
        XX{x} = X;
        [r, c] = size(X);
        Y{x} = ones( [1, c] ); % female label is 1
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

fprintf( 'Final data matrix: \n' );
[ y, x ] = size(XX);
XXX = [ XX{1:x} ];
YYY = [ Y{1:x} ];
clear XX Y;
size( XXX )
XXX_T = transpose(XXX);

% Independent Component Analysis to extract P primary components.
% The rest is considered noise.
Mdl_2 = rica( XXX_T, 3, 'IterationLimit', 1000 );
XXX2_T = XXX_T * Mdl_2.TransformWeights;
XXX2 = transpose( XXX2_T );
clear XXX XXX_T XXX2_T
fprintf( 'ICA 3-component training matrix:\n');
size(XXX2)


%-------- Train Models ------------------------------------------------
[y1, GMM, L] = mixGaussVb( XXX2, 3 );
fprintf ( "3-Class GMM trained:\n" );
GMM
%clear XXX;

%------- 2 class Gaussian MM
%[y2, GMM2, L2] = mixGaussVb( XXX2, 2 );
%fprintf ( "3-Class GMM trained:\n" );
%GMM2


%------------- Test dataset performance -----------------------
[c, n] = size(names_m);
x = 0;
first_round = true;
count_m = zeros(3);
count_f = zeros(3);
fc_m = zeros([1,3]);
fc_f = zeros([1,3]);
fprintf( '----------------- Test Dataset -----------------\n\n');
[r, male_test_max] = size( speaker_m);
[r, female_test_max] = size( speaker_f);
nm = 1181;
nf = 826;
for i = N+1:n
    while ( (nm <= male_test_max)  &&  strcmp( speaker_m{ nm }, names_m{i} ) )
        datf = fullfile( data_dir, speaker_m{nm}, file_m{nm} );
        [ X, period, paramkind ] = readhtk_lite( datf );
        ICA = X * Mdl_2.TransformWeights;
        X = transpose( ICA );
        [Y, R] = mixGaussVbPred( GMM, X);
        c1 = sum(Y==1);
        c2 = sum( Y==2 );
        c3 = sum( Y==3);
        max = c1;
        mi = 1;
        if ( c2 > max ) 
            max = c2; 
            mi = 2;
        end
        if ( c3 > max ) 
            max = c3; 
            mi = 3;
        end
        count_m(mi) = count_m(mi) + 1;
        fc_m(1) = fc_m(1) + c1;
        fc_m(2) = fc_m(2) + c2;
        fc_m(3) = fc_m(3) + c3;
        fprintf( '[%u] Male segment, c1: %u  c2: %u  c3: %u  Class: %u\n', i, c1, c2, c3, mi );
        nm = nm +1;
    end
    % female
    while ( (nf <= female_test_max)  &&  strcmp( speaker_f{ nf }, names_f{i} ) )
        datf = fullfile( data_dir, speaker_f{nf}, file_f{nf} );
        [ X, period, paramkind ] = readhtk_lite( datf );
        ICA = X * Mdl_2.TransformWeights;
        X = transpose( ICA );
        [Y, R] = mixGaussVbPred( GMM, X);
        c1 = sum(Y==1);
        c2 = sum( Y==2 );
        c3 = sum( Y==3);
        max = c1;
        mi = 1;
        if ( c2 > max ) 
            max = c2; 
            mi = 2;
        end
        if ( c3 > max ) 
            max = c3; 
            mi = 3;
        end
        count_f(mi) = count_f(mi) + 1;
        fc_f(1) = fc_f(1) + c1;
        fc_f(2) = fc_f(2) + c2;
        fc_f(3) = fc_f(3) + c3;
        fprintf( '[%u] Female segment, c1: %u  c2: %u  c3: %u  Class: %u\n', i, c1, c2, c3, mi );
        nf = nf + 1;
    end
end

% fprintf( 'Training Data 3-Class Frame classification:\n' );
% males = ( (YYY==0).*(y1) );
% females = ( (YYY==1).*(y1) );
% fprintf( 'Male  :  %u  %u  %u\n', sum(males==1), sum(males==2),  sum(males==3) );
% fprintf( 'Female:  %u  %u  %u\n', sum(females==1), sum(females==2),  sum(females==3) );
% fprintf('\n' );
% 
% fprintf( '\nFrame classification.\n' );
% fprintf( 'Male  :  %u  %u  %u\n', fc_m(1), fc_m(2), fc_m(3)  );
% fprintf( 'Female:  %u  %u  %u\n', fc_f(1), fc_f(2), fc_f(3)  );
% fprintf('\n' );

fprintf( '\nSegment classification.\n' );
fprintf( 'Male  :  %u  %u  %u\n', count_m(1), count_m(2), count_m(3) );
fprintf( 'Female:  %u  %u  %u\n', count_f(1), count_f(2), count_f(3) );


