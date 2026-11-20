<template>
  <!-- 页面水印层：防篡改（DOM 观察+自愈），pointer-events 穿透不挡交互 -->
  <div v-if="wm.enabled" ref="layerRef" class="watermark-layer" :style="layerStyle" aria-hidden="true" />
</template>

<script setup lang="ts">
import { ref, computed, watch, onMounted, onBeforeUnmount } from 'vue';
import { useWatermarkStore } from '@/stores/watermark';
import { useUserStore } from '@/stores/user';
import { useThemeStore } from '@/stores/theme';

const wm = useWatermarkStore();
const userStore = useUserStore();
const themeStore = useThemeStore();

const layerRef = ref<HTMLDivElement | null>(null);
let observer: MutationObserver | null = null;

const layerStyle = ref<Record<string, string>>({});
/** 原父容器：被外部删除后重挂回原位（W1-14 自愈） */
const hostParent = ref<HTMLElement | null>(null);

/** 时间戳带分钟：截图可溯源（设计定稿三） */
function stamp(): string {
  const d = new Date();
  const p = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())} ${p(d.getHours())}:${p(d.getMinutes())}`;
}

function renderWatermark() {
  const el = layerRef.value;
  if (!el) return;
  const text = `${userStore.nickname} · ${stamp()}`;
  const canvas = document.createElement('canvas');
  canvas.width = 320;
  canvas.height = 200;
  const ctx = canvas.getContext('2d');
  if (ctx) {
    ctx.rotate(-18 * (Math.PI / 180));
    ctx.font = '14px sans-serif';
    ctx.fillStyle = themeStore.isDark ? 'rgba(255, 255, 255, 0.08)' : 'rgba(0, 0, 0, 0.06)';
    ctx.fillText(text, 24, 110);
  }
  layerStyle.value = {
    backgroundImage: `url(${canvas.toDataURL('image/png')})`,
    backgroundRepeat: 'repeat',
  };
}

/** 防篡改：容器被删/移出/属性被改 → 自愈重挂或重渲染（W1-03/W1-14） */
function setupObserver() {
  if (observer) observer.disconnect();
  if (!wm.enabled) return;
  observer = new MutationObserver(() => {
    if (!wm.enabled) return;
    const el = layerRef.value;
    if (!el) return;
    if (!document.body.contains(el)) {
      // 自愈：组件未卸载但 DOM 被外部删除 → 重挂回原父容器（body 兜底）；
      // 重挂动作本身会再次触发观察器，return 防观察风暴
      hostParent.value?.appendChild(el);
      return;
    }
    // 属性被清 → 重绘；已有背景不重复渲染，防 patch-观察死循环
    if (!el.style.backgroundImage) renderWatermark();
  });
  observer.observe(document.body, { childList: true, attributes: true, subtree: true });
}

function teardownObserver() {
  observer?.disconnect();
  observer = null;
}

watch(
  () => wm.enabled,
  (v) => {
    if (v) {
      renderWatermark();
      setupObserver();
    } else {
      teardownObserver();
    }
  }
);

watch(() => userStore.nickname, renderWatermark);
watch(() => themeStore.isDark, renderWatermark);

onMounted(() => {
  hostParent.value = layerRef.value?.parentElement || null;
  if (wm.enabled) {
    renderWatermark();
    setupObserver();
  }
});

onBeforeUnmount(() => {
  // 卸载前确保元素在原位，Vue removeChild 不踩空
  const el = layerRef.value;
  if (el && hostParent.value && !document.body.contains(el)) hostParent.value.appendChild(el);
  teardownObserver();
});
</script>

<style scoped>
.watermark-layer {
  position: fixed;
  inset: 0;
  z-index: 999; /* 内容之上、antd 弹层(1000)与锁屏(2000)之下（设计定稿四） */
  pointer-events: none;
}
</style>
