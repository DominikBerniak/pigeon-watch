import { Observable, concatMap, filter, from, interval, map, take, tap, timeout } from 'rxjs';

export interface CellContext {
  readonly root: HTMLElement;
  readonly text: (key: string) => string;
}

export type CellStep = (context: CellContext) => Observable<unknown>;

const pollMilliseconds = 20;
export const waitLimitMilliseconds = 5000;

export function until<T>(read: () => T | null): Observable<T> {
  return interval(pollMilliseconds).pipe(
    map(() => read()),
    filter((value): value is T => value !== null),
    take(1),
    timeout(waitLimitMilliseconds),
  );
}

export function runSteps(steps: readonly CellStep[], context: CellContext): Observable<unknown> {
  return from(steps).pipe(concatMap((step) => step(context)));
}

export function press(labelKey: string): CellStep {
  return ({ root, text }) =>
    until(() => buttonWithText(root, text(labelKey))).pipe(tap((button) => button.click()));
}

export function click(selector: string): CellStep {
  return ({ root }) =>
    until(() => root.querySelector<HTMLElement>(selector)).pipe(tap((element) => element.click()));
}

export function keydown(selector: string, key: string): CellStep {
  return ({ root }) =>
    until(() => root.querySelector<HTMLElement>(selector)).pipe(
      tap((element) => {
        element.focus();
        element.dispatchEvent(
          new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }),
        );
      }),
    );
}

export function repeat(times: number, ...steps: CellStep[]): CellStep[] {
  return Array.from({ length: times }, () => steps).flat();
}

export function fill(selector: string, values: readonly string[]): CellStep {
  return ({ root }) =>
    until(() => {
      const inputs = [...root.querySelectorAll<HTMLInputElement>(selector)];

      return inputs.length >= values.length ? inputs : null;
    }).pipe(tap((inputs) => values.forEach((value, index) => setValue(inputs[index], value))));
}

export function submitForm(): CellStep {
  return ({ root }) =>
    until(() => root.querySelector('form')).pipe(
      tap((form) => form.dispatchEvent(new Event('submit', { cancelable: true }))),
    );
}

export function waitFor(selector: string): CellStep {
  return ({ root }) => until(() => root.querySelector(selector));
}

export function waitForText(selector: string, labelKey: string): CellStep {
  return ({ root, text }) =>
    until(() => {
      const element = root.querySelector(selector);

      return element?.textContent?.trim() === text(labelKey) ? element : null;
    });
}

function buttonWithText(root: HTMLElement, label: string): HTMLButtonElement | null {
  return (
    [...root.querySelectorAll<HTMLButtonElement>('button[type="button"]')].find(
      (button) => button.textContent?.trim() === label,
    ) ?? null
  );
}

function setValue(input: HTMLInputElement, value: string): void {
  input.value = value;
  input.dispatchEvent(new Event('input'));
  input.dispatchEvent(new Event('blur'));
}
