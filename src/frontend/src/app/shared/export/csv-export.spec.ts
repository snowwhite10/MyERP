import { countCsvDataRows } from './csv-export';

describe('SD-ORDER-EXPORT-UI-003 countCsvDataRows', () => {
  it('counts CRLF data rows after the header and ignores a UTF-8 BOM', () => {
    expect(countCsvDataRows('﻿a,b\r\n1,2\r\n3,4\r\n')).toBe(2);
  });

  it('counts a quoted embedded comma, quote and newline as one row', () => {
    expect(countCsvDataRows('a,b\r\n"x,""y""\r\nz",2\r\n')).toBe(1);
  });

  it('accepts a last row without a terminator and LF-only rows', () => {
    expect(countCsvDataRows('a,b\n1,2\n3,4')).toBe(2);
  });

  it('returns zero for header only and null for an empty or unterminated file', () => {
    expect(countCsvDataRows('a,b\r\n')).toBe(0);
    expect(countCsvDataRows('')).toBeNull();
    expect(countCsvDataRows('﻿')).toBeNull();
    expect(countCsvDataRows('a,b\r\n"open,1\r\n')).toBeNull();
  });
});
