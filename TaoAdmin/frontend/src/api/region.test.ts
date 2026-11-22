import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

// mock request 层：按 URL 记录调用并返回预设数据
const calls: Array<{ url: string; method: string; data?: unknown; params?: unknown }> = [];
vi.mock('./request', () => ({
  request: vi.fn(async (cfg: { url: string; method: string; data?: unknown; params?: unknown }) => {
    calls.push(cfg);
    if (cfg.url.includes('regions/children'))
      return [{ code: '110101001', name: '东华门街道', parentCode: '110101', level: 4, fullPath: '北京市/东城区/东华门街道' }];
    if (cfg.url.includes('regions/search'))
      return [{ code: '110101', name: '东城区', parentCode: '1101', level: 3, fullPath: '北京市/东城区' }];
    if (cfg.url.includes('regions/status'))
      return {
        version: '民政部国家地名信息库·数据截止2025年12月31日',
        source: '民政部一体化政务服务平台行政区划查询接口',
        fetchedAt: '2026-11-22 10:00:00',
        provinceCount: 33,
        cityCount: 333,
        countyCount: 2845,
        townCount: 38723,
        totalCount: 41934,
        update: { status: 'Idle', stage: '完成', percent: 100, fetchedCount: 41934, sourceKind: 'online' },
      };
    if (cfg.url.includes('regions/update/progress'))
      return { status: 'Running', stage: '抓取数据', percent: 40, fetchedCount: 12000, sourceKind: 'online' };
    return { started: true };
  }),
}));

import {
  getRegionChildren,
  searchRegions,
  getRegionByCode,
  getRegionStatus,
  startRegionUpdate,
  restoreRegionBuiltin,
  getRegionUpdateProgress,
} from './region';

beforeEach(() => { calls.length = 0; });
afterEach(() => { vi.clearAllMocks(); });

describe('行政区划接口封装', () => {
  it('子级查询 GET /regions/children，parentCode 透传', async () => {
    const list = await getRegionChildren('110101');
    expect(list[0].name).toBe('东华门街道');
    expect(calls[0]).toMatchObject({ url: '/identity/regions/children', method: 'get', params: { parentCode: '110101' } });
    await getRegionChildren();
    expect(calls[1].params).toMatchObject({ parentCode: '' });
  });

  it('搜索 GET /regions/search 携带关键词', async () => {
    const list = await searchRegions('东城');
    expect(list[0].code).toBe('110101');
    expect(calls[0]).toMatchObject({ url: '/identity/regions/search', method: 'get', params: { keyword: '东城' } });
  });

  it('详情 GET /regions/{code}', async () => {
    await getRegionByCode('110101');
    expect(calls[0]).toMatchObject({ url: '/identity/regions/110101', method: 'get' });
  });

  it('状态查询 GET /regions/status 返回版本与四级条数', async () => {
    const s = await getRegionStatus();
    expect(s.totalCount).toBe(41934);
    expect(s.provinceCount).toBe(33);
    expect(s.update.status).toBe('Idle');
    expect(calls[0]).toMatchObject({ url: '/identity/regions/status', method: 'get' });
  });

  it('在线更新 POST /regions/update；内置恢复 POST /regions/update/builtin', async () => {
    await startRegionUpdate();
    expect(calls[0]).toMatchObject({ url: '/identity/regions/update', method: 'post' });
    await restoreRegionBuiltin();
    expect(calls[1]).toMatchObject({ url: '/identity/regions/update/builtin', method: 'post' });
  });

  it('进度轮询 GET /regions/update/progress', async () => {
    const p = await getRegionUpdateProgress();
    expect(p.status).toBe('Running');
    expect(p.percent).toBe(40);
    expect(calls[0]).toMatchObject({ url: '/identity/regions/update/progress', method: 'get' });
  });
});
