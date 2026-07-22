import { DotnetSharedObject, requireDotnetModule, type EventSubscription } from '../index';

type CounterEvents = {
  onProgress(value: number): void;
};

declare class CounterHandle extends DotnetSharedObject<CounterEvents> {
  increment(by: number): number;
}

declare class CounterModule {
  CounterHandle: typeof CounterHandle;
  makeCounter(start: number): CounterHandle;
}

const module = requireDotnetModule<CounterModule>('CounterModule');
const constructed: CounterHandle = new module.CounterHandle();
const returned: CounterHandle = module.makeCounter(1);

// Subclasses inherit the shared-object release surface.
const released: void = constructed.release();
returned.release();
const subscription: EventSubscription = constructed.addListener('onProgress', value => {
  const typedValue: number = value;
  return typedValue;
});

// @ts-expect-error shared-object event names are typed per class.
constructed.addListener('missing', () => {});

// @ts-expect-error shared-object event payloads are typed per class.
constructed.addListener('onProgress', (value: string) => value);

// @ts-expect-error release takes no arguments.
returned.release('now');

// @ts-expect-error release returns void.
const misused: number = constructed.release();

export { released, misused, subscription };
