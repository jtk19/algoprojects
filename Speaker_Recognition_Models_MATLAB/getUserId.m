%--------------------------------------------------------------------------
% Function to extract speaker ID from Prudential filename.
function id = getUserId( filename )
    s = split( filename, '-EXACT.' );
    s1 = split( s(1), '-' );
    id = char( s1(3) ); 
end