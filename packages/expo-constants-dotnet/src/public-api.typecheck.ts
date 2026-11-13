import type constants from './index';
import type { ExpoConstants } from './index';

type Assert<T extends true> = T;

type ManifestIsANullableString = Assert<
  ExpoConstants['manifest'] extends string | null
    ? string | null extends ExpoConstants['manifest']
      ? true
      : false
    : false
>;

type NoExtraPublicKeys = Assert<
  Exclude<keyof typeof constants, keyof ExpoConstants> extends never ? true : false
>;
