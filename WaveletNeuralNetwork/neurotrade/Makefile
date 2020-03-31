
CXXFLAGS =	-O2 -g -Wall -fmessage-length=0 $(INCS)

OBJS =		neurotrade.o def.o neurotrdb.o WaveletNN.o KMeansClustering.o

LIBS =		-L. -L$(LEDAROOT) -lleda -lX11 -lodbc 

INCS = 		-I$(LEDAROOT)/incl 

TARGET =	neurotrade


$(TARGET):	$(OBJS)
	$(CXX) -o $(TARGET) $(INCS) $(LIBS) $(OBJS)
	

all:	$(TARGET)


clean:
	rm -f $(OBJS) $(TARGET) $(TARGET1) $(OBJLIBS)
