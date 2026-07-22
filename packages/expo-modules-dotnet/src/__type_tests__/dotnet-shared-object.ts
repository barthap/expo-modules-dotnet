import { DotnetSharedObject, requireDotnetModule } from '../index';

type CounterEvents = {
  onChange(value: number): void;
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

constructed.addListener('onChange', (value) => value.toFixed());

// @ts-expect-error event name is not declared by this shared-object class.
constructed.addListener('missing', () => {});

// @ts-expect-error onChange listeners receive a number.
constructed.addListener('onChange', (value: string) => value);

// @ts-expect-error release takes no arguments.
returned.release('now');

// @ts-expect-error release returns void.
const misused: number = constructed.release();

export { released, misused };
