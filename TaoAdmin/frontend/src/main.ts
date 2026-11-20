import { createApp } from 'vue';
import { createPinia } from 'pinia';
import Antd from 'ant-design-vue';
import 'ant-design-vue/dist/reset.css';
import App from './App.vue';
import router from './router';
import { setupPermissionDirective } from './directives/permission';
import './styles/index.less';

const app = createApp(App);

app.use(createPinia());
app.use(router);
app.use(Antd);

// 注册 v-permission 指令
setupPermissionDirective(app);

app.mount('#app');
