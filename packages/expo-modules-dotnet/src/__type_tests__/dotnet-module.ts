import {
  DotnetEventEmitter,
  DotnetModule,
  requireDotnetModule,
  type EventSubscription,
} from '../index';

type FixtureEvents = {
  onFoo(payload: string): void;
  onPair(left: number, right: boolean): void;
};

declare class Fixture extends DotnetModule<FixtureEvents> {
  add(left: number, right: number): number;
}

declare class EventOnlyFixture extends DotnetEventEmitter<FixtureEvents> {}

const module = requireDotnetModule<Fixture>('Fixture');
const subscription: EventSubscription = module.addListener('onFoo', payload => {
  const value: string = payload;
  return value;
});

module.removeListener('onFoo', () => {});
module.removeAllListeners('onFoo');
module.emit('onPair', 1, true);
const listenerTotal: number = module.listenerCount('onFoo');
const sum: number = module.add(1, 2);
const eventOnlyTotal: number = (null as unknown as EventOnlyFixture).listenerCount('onPair');
subscription.remove();

void listenerTotal;
void sum;
void eventOnlyTotal;

// @ts-expect-error Event names must be declared by FixtureEvents.
module.addListener('onMissing', () => {});
// @ts-expect-error Listener arguments must match the selected event.
module.addListener('onFoo', (payload: number) => {});
// @ts-expect-error Emitted arguments must match the selected event tuple.
module.emit('onPair', 1, 'not-a-boolean');
// @ts-expect-error The legacy runtime helper is deliberately not part of the modern facade.
module.removeSubscription(subscription);
