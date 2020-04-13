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

format long g
fprintf( '\nSelected [%d] with %d mixtures. BIC Scores of SI models trained:\n', best_i, mix(best_i) );
fprintf( '                    Mixture #        GMM BIC\n' );
disp( [reshape( mix, M, 1) reshape( si_BIC, M, 1 )] );
% clear si_gmm si_BIC;

% Important take-forwards at this point: ICA transform, 
% XAll_ICA, samples transformd to ICA 10 dimenstions
% and SI_GMM, the Speaker Independent model for Speaker Adaptation.


%--------------------------------------------------------------------------
% Process speaker labels
clear GMM
gmm_options = statset('MaxIter',1000);
[ r numspf ] = size( spfiles );
for i = 1:numspf
    uid{i} = getUserId( spfiles( 1, i ) );
    ufile{i} = char( spfiles( 1, i ) );
end
uuid = unique(uid);
[ r numu ] = size(uuid);
for i = 1:numu 
    % For each known speaker, build a speaker model
    clear XX
    segcount = 0;
    for j = 1:numspf
        if ( string(uuid{i}) == string(uid{j}) )
            clear X;
            datf = fullfile( pdata_dir, ufile{j} );
            fprintf( '[%d] %s\n', j, datf );
            [ X, period, paramkind ] = readhtk_lite( datf );
            % delete silence rows at the end 
            del = sum( X( :, 1:10), 2 ) == 0;
            X( del, : ) = [];
            %--
            segcount = segcount + 1;
            XX{segcount} = transpose(X);
        end
    end
    % Build speaker model
    XXX = [ XX{1:segcount} ];
    clear X XX del
    sprintf( 'Building speaker model for speaker [%s] with data:', uuid{i} );
    XXX = transpose(XXX);
    XICA = XXX * ICA.TransformWeights;
    size( XICA )
    clear XXX;
    
    mix = [ 2 4 6 8 ];
    M = length( mix );
    f_BIC = zeros( 1, M);
    best_m = 0;
    best_val = 999999999999999999;
    for m = 1:M
        k = mix(m);
        fprintf( 'Training female GMM for %d mixtures. . . \n', k);
        sp_gmm{m} = fitgmdist( XICA, k, 'RegularizationValue', 0.05, 'Options', gmm_options);
        f_BIC(i) = sp_gmm{m}.BIC;
        if ( f_BIC(m) < best_val )
            best_val = f_BIC(m);
            best_m = m;
        end
    end
    GMM{i} = sp_gmm{best_m};
    clear sp_gmm XICA;
end


%--------------------------------------------------------------------------
% Evaluate segment distances to Speaker models
[ y, c] = size(pall_flist);
for i = 1:y
   fprintf( 'Speaker Model for %s, Bhattacharyya coefficient of segments:\n', uuid{i} ); 
   for j = 1:y
        datf = fullfile( pdata_dir, pall_flist(j).name );
        [ X, period, paramkind ] = readhtk_lite( datf );
        % delete silence rows at the end 
        del = sum( X( :, 1:10), 2 ) == 0;
        X( del, : ) = [];
        % --
        XICA = X * ICA.TransformWeights;
        bhatt_coeff = bhatt_coefficient( GMM{i}, XICA );
        fprintf( '    %s Bhatt coefficient: %f\n', pall_flist(j).name, bhatt_coeff );
   end
   fprintf( '\n' );
end
