import { DotnetSharedObject, requireDotnetModule } from '../index';

declare class CounterHandle extends DotnetSharedObject {
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

// @ts-expect-error release takes no arguments.
returned.release('now');

// @ts-expect-error release returns void.
const misused: number = constructed.release();

export { released, misused };
