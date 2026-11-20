# TaoAdmin K8s 部署清单

使用顺序：
1. `kubectl apply -f namespace.yaml`
2. `kubectl apply -f configmap.yaml`
3. 复制 `secret.example.yaml` 为 `secret.yaml`，填入真实凭据后 `kubectl apply -f secret.yaml`
4. `kubectl apply -f gateway.yaml identity.yaml task.yaml frontend.yaml`
5. `kubectl apply -f ingress.yaml`（按实际域名/IngressClass 调整）

日志查看：直接在 Kibana（ELK 由独立部署或 compose 提供）按服务名过滤，
后端 Serilog Elasticsearch sink 的索引模式为 `logstash-yyyy.MM.dd`。
