.PHONY: gen build test run clean

PROTO_DIR := ../../protos
OUT_DIR   := pb
GOPATH_BIN := $(shell go env GOPATH)/bin
PATH_AUG := PATH="$$PATH:$(GOPATH_BIN)"

gen:
	mkdir -p $(OUT_DIR)
	$(PATH_AUG) protoc -I $(PROTO_DIR) \
		--go_out=$(OUT_DIR) --go_opt=paths=source_relative \
		--go-grpc_out=$(OUT_DIR) --go-grpc_opt=paths=source_relative \
		$(PROTO_DIR)/preprocessing.proto

build:
	go build -o bin/preprocessor .

test:
	go test ./...

run: build
	./bin/preprocessor

clean:
	rm -rf bin $(OUT_DIR)/*.pb.go
