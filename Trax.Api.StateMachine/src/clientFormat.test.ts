import { describe, expect, it } from 'vitest';
import { clientFormat, oldestClientFormatServed, requireClientFormat } from './clientFormat';

describe('the client format', () => {
  it('serves the format this runtime writes', () => {
    expect(() => requireClientFormat(clientFormat)).not.toThrow();
  });

  it('refuses a newer format on load, naming both and telling to upgrade the package', () => {
    expect(() => requireClientFormat(clientFormat + 1)).toThrow(
      new RegExp(`format ${clientFormat + 1}.*up to format ${clientFormat}.*Upgrade @traxsharp/state-machine`),
    );
  });

  it('refuses a format older than it serves, telling to regenerate', () => {
    expect(() => requireClientFormat(oldestClientFormatServed - 1)).toThrow(/no longer serves.*trax client/);
  });

  it('still serves the first format, which clients generated before it were written in', () => {
    expect(() => requireClientFormat(1)).not.toThrow();
  });

  it('refuses something that is not a format number', () => {
    expect(() => requireClientFormat(1.5)).toThrow(/not a format number/);
  });
});
