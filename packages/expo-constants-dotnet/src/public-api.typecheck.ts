import type constants from './index';
import type { ExpoConstants } from './index';

type Assert<T extends true> = T;

type NoExtraPublicKeys = Assert<
  Exclude<keyof typeof constants, keyof ExpoConstants> extends never ? true : false
>;
