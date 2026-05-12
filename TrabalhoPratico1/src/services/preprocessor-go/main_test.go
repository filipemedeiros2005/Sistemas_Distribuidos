package main

import (
	"math"
	"testing"

	pb "onehealth/preprocessor/pb"
)

func TestNormalize_BoundsAndUnits(t *testing.T) {
	cases := []struct {
		name        string
		in          *pb.RawMeasurement
		wantValue   float64
		wantDropped bool
		wantReason  string
	}{
		{
			name:      "temp valid celsius passes through",
			in:        &pb.RawMeasurement{DataType: "TEMP", Value: 22.5},
			wantValue: 22.5,
		},
		{
			name:      "temp fahrenheit converts to celsius",
			in:        &pb.RawMeasurement{DataType: "TEMP", Value: 68.0, UnitHint: "F"},
			wantValue: 20.0,
		},
		{
			name:      "temp kelvin converts to celsius",
			in:        &pb.RawMeasurement{DataType: "TEMP", Value: 298.15, UnitHint: "K"},
			wantValue: 25.0,
		},
		{
			name:        "temp above bound dropped",
			in:          &pb.RawMeasurement{DataType: "TEMP", Value: 85.0},
			wantValue:   85.0,
			wantDropped: true,
			wantReason:  "out_of_bounds:TEMP",
		},
		{
			name:        "temp below bound dropped",
			in:          &pb.RawMeasurement{DataType: "TEMP", Value: -50.0},
			wantValue:   -50.0,
			wantDropped: true,
			wantReason:  "out_of_bounds:TEMP",
		},
		{
			name:      "humidity at upper bound passes",
			in:        &pb.RawMeasurement{DataType: "HUM", Value: 100.0},
			wantValue: 100.0,
		},
		{
			name:        "humidity above bound dropped",
			in:          &pb.RawMeasurement{DataType: "HUM", Value: 120.0},
			wantValue:   120.0,
			wantDropped: true,
			wantReason:  "out_of_bounds:HUM",
		},
		{
			name:        "noise above pain threshold dropped",
			in:          &pb.RawMeasurement{DataType: "RUIDO", Value: 160.0},
			wantValue:   160.0,
			wantDropped: true,
			wantReason:  "out_of_bounds:RUIDO",
		},
		{
			name:      "pm25 valid passes",
			in:        &pb.RawMeasurement{DataType: "PM25", Value: 42.0},
			wantValue: 42.0,
		},
		{
			name:        "pm10 saturation dropped",
			in:          &pb.RawMeasurement{DataType: "PM10", Value: 1500.0},
			wantValue:   1500.0,
			wantDropped: true,
			wantReason:  "out_of_bounds:PM10",
		},
		{
			name:        "nan dropped",
			in:          &pb.RawMeasurement{DataType: "TEMP", Value: math.NaN()},
			wantDropped: true,
			wantReason:  "nan_or_inf",
		},
		{
			name:        "inf dropped",
			in:          &pb.RawMeasurement{DataType: "HUM", Value: math.Inf(1)},
			wantDropped: true,
			wantReason:  "nan_or_inf",
		},
		{
			name:      "unknown datatype passes value unchanged",
			in:        &pb.RawMeasurement{DataType: "MYSTERY", Value: 9001.0},
			wantValue: 9001.0,
		},
		{
			name:      "datatype lowercased gets upper-cased on output",
			in:        &pb.RawMeasurement{DataType: "temp", Value: 25.0},
			wantValue: 25.0,
		},
	}

	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			got := normalize(tc.in)
			if tc.wantDropped {
				if !got.Dropped {
					t.Fatalf("expected dropped=true, got dropped=false (reason=%q, value=%v)", got.DropReason, got.Value)
				}
				if got.DropReason != tc.wantReason {
					t.Fatalf("drop reason: want %q, got %q", tc.wantReason, got.DropReason)
				}
				return
			}
			if got.Dropped {
				t.Fatalf("expected not dropped, got dropped (reason=%q)", got.DropReason)
			}
			if math.Abs(got.Value-tc.wantValue) > 1e-6 {
				t.Fatalf("value: want %v, got %v", tc.wantValue, got.Value)
			}
		})
	}
}

func TestNormalizeBatch_AllItems(t *testing.T) {
	srv := &server{}
	req := &pb.BatchRequest{
		Items: []*pb.RawMeasurement{
			{DataType: "TEMP", Value: 20.0},
			{DataType: "TEMP", Value: 100.0},
		},
	}
	resp, err := srv.NormalizeBatch(nil, req)
	if err != nil {
		t.Fatalf("unexpected error: %v", err)
	}
	if len(resp.Items) != 2 {
		t.Fatalf("expected 2 items, got %d", len(resp.Items))
	}
	if resp.Items[0].Dropped {
		t.Fatalf("first item should pass")
	}
	if !resp.Items[1].Dropped {
		t.Fatalf("second item should be dropped (out of bounds)")
	}
}
