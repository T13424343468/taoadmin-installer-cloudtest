# ============ 构建阶段 ============
# node:20 自带 npm 10.8 有 arborist edgesOut 空引用 bug；node:22 的 npm 无此问题且满足 rolldown 引擎要求
FROM node:22-alpine AS builder

WORKDIR /app

# 先复制依赖文件，利用 Docker 缓存
COPY package.json ./
# npmmirror 偶发 arborist edgesOut 空引用（npm 已知 bug），失败时清缓存回退官方源
RUN npm install --registry=https://registry.npmmirror.com || (npm cache clean -f && npm install)

# 复制源码并构建
COPY . .
RUN npm run build:fast

# ============ 运行阶段 ============
FROM nginx:1.27-alpine

# 替换 Nginx 配置：SPA 路由回退 + /api 反向代理到网关
COPY nginx.conf /etc/nginx/conf.d/default.conf

# 复制构建产物
COPY --from=builder /app/dist /usr/share/nginx/html

EXPOSE 80

CMD ["nginx", "-g", "daemon off;"]
