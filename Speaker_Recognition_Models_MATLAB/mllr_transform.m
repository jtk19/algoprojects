function W = mllr_transform( GMM, O )
% Estimates a linear transform A and a bias b on the GMM passed in
% such that [ b A ] maximises the likelihood of the adaptation data O.
% GMM: A Gaussian Mixture Models with m mixtures and 
%        diagonal covariance matrices
% O: A T x K matrix of T observations of D dimension.
% W = [ b A ] where b is the bias vector of K dimenstion 
%       and A is a D x K linear transform.
%------------------
    R = GMM.NumComponents;
    [ T, K ] = size( O );
    L = posterior( GMM, O );                    % T x R
    E_t = [ ones( R, 1 ) GMM.mu  ];             % R x ( 1 + D ) = 18 x 11
    L1 = sum( L, 1 );                                       % 1
    clear V D;
    Z = zeros( K, K + 1 );
    for r = 1:R
        C_r_inv = inv( diag( GMM.Sigma( :, :, r ) ) );
        V{r} = L1(r) *  C_r_inv;                            % 10 x 10
        m_r_t = E_t( r, : );                                %  1 x 11
        D{r} = transpose( m_r_t ) * m_r_t ;                 % 11 x 11
        for t = 1:T
            z = L( t, r ) * C_r_inv * transpose( O(t,:) ) * E_t( r, : );
            Z = Z + z;
        end
    end
   
    for i = 1:K
        G{i} = zeros( K + 1, K + 1 );
        for j = 1:(K+1)
            for q = 1:(K+1)
                for r = 1:R
                    G{i}( j, q ) = G{i}( j, q ) + V{r}(i,i) * D{r}( j, q );
                end
            end
        end
    end
    
    W = zeros( K, K + 1 );
    for i = 1:K
        w_T = inv( G{i} ) * transpose( Z( i, : ) );
        W( i, : ) = transpose( w_T );
    end

end