function bc = bhatt_coefficient( model, M )
% Gives the Bhattacharyya coefficient of a set of samples to the given model.
% model: the GMM or other model 
% M: the n by d matrix where n is the samples along the rows
%       and d is the features along the columns
    ym = pdf( model, M ); 
    bc = sum( sqrt( ym ) );
end