package main

import (
	"context"
	"flag"
	"log"
	"math"
	"net"
	"os"
	"os/signal"
	"strings"
	"syscall"

	pb "onehealth/preprocessor/pb"

	"google.golang.org/grpc"
)

type server struct {
	pb.UnimplementedPreProcessorServer
}

type bounds struct {
	min float64
	max float64
}

var dataTypeBounds = map[string]bounds{
	"TEMP":  {-40.0, 70.0},
	"HUM":   {0.0, 100.0},
	"RUIDO": {0.0, 140.0},
	"LUM":   {0.0, 150000.0},
	"PM25":  {0.0, 1000.0},
	"PM10":  {0.0, 1000.0},
}

func normalize(in *pb.RawMeasurement) *pb.NormalizedMeasurement {
	out := &pb.NormalizedMeasurement{
		SensorId: in.SensorId,
		DataType: strings.ToUpper(in.DataType),
		UnixTs:   in.UnixTs,
		Zona:     in.Zona,
		Value:    in.Value,
	}

	if math.IsNaN(in.Value) || math.IsInf(in.Value, 0) {
		out.Dropped = true
		out.DropReason = "nan_or_inf"
		return out
	}

	out.Value = convertUnit(out.DataType, in.UnitHint, in.Value)

	if b, ok := dataTypeBounds[out.DataType]; ok {
		if out.Value < b.min || out.Value > b.max {
			out.Dropped = true
			out.DropReason = "out_of_bounds:" + out.DataType
		}
	}
	return out
}

func convertUnit(dataType, hint string, value float64) float64 {
	if dataType != "TEMP" {
		return value
	}
	switch strings.ToUpper(hint) {
	case "F":
		return (value - 32.0) * 5.0 / 9.0
	case "K":
		return value - 273.15
	default:
		return value
	}
}

func (s *server) Normalize(_ context.Context, in *pb.RawMeasurement) (*pb.NormalizedMeasurement, error) {
	return normalize(in), nil
}

func (s *server) NormalizeBatch(_ context.Context, in *pb.BatchRequest) (*pb.BatchResponse, error) {
	out := &pb.BatchResponse{Items: make([]*pb.NormalizedMeasurement, 0, len(in.Items))}
	for _, item := range in.Items {
		out.Items = append(out.Items, normalize(item))
	}
	return out, nil
}

func main() {
	defaultPort := "50051"
	if v := os.Getenv("PREPROC_PORT"); v != "" {
		defaultPort = v
	}
	port := flag.String("port", defaultPort, "TCP port to listen on")
	flag.Parse()

	addr := ":" + *port
	lis, err := net.Listen("tcp", addr)
	if err != nil {
		log.Fatalf("listen %s: %v", addr, err)
	}
	grpcServer := grpc.NewServer()
	pb.RegisterPreProcessorServer(grpcServer, &server{})

	sigCh := make(chan os.Signal, 1)
	signal.Notify(sigCh, syscall.SIGINT, syscall.SIGTERM)
	go func() {
		<-sigCh
		log.Println("[preprocessor] shutting down")
		grpcServer.GracefulStop()
	}()

	log.Printf("[preprocessor] gRPC listening on %s", addr)
	if err := grpcServer.Serve(lis); err != nil {
		log.Fatalf("serve: %v", err)
	}
}
