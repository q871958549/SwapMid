# Kafka Event-Stream Sink

This example provisions a **Kafka (`DataSourceType = 3`)** streaming sink producing JSON payloads with bounded-memory batch delivery and configurable SASL/TLS posture.

## Security Prerequisite

Explicitly allowlist broker hosts and VPC private CIDR ranges before probing brokers or activating streaming pipelines:

```bash
DataIntegration__Sinks__AllowedHosts=kafka
DataIntegration__Sinks__AllowedPrivateNetworks=10.0.0.0/8,172.16.0.0/12,192.168.0.0/16
```

## Steps

1. Register the Kafka cluster from [`datasource.json`](./datasource.json) and verify broker metadata reachability with **Test Connection**.
2. Create the pipeline from [`pipeline.json`](./pipeline.json), verify projected event schema in **Schema Pre-flight**, activate the pipeline, and run.
