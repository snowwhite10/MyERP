import { HttpErrorResponse, provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { FACTOR_PATTERN, QUANTITY_PATTERN, UOM_API, UOM_CONVERSION_API, UomService } from './uom.service';

describe('MD-UOM-003 UomService', () => {
  let service: UomService;
  let http: HttpTestingController;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    service = TestBed.inject(UomService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('calls the unit and conversion endpoints with normalized bodies', () => {
    service.list({ page: 2, pageSize: 50, keyword: '  ', sort: '-name' }).subscribe();
    expect(http.expectOne((r) => r.url === UOM_API).request.params.toString()).toBe('page=2&pageSize=50&sort=-name');
    service.create({ code: ' kg ', name: ' Kilogram ', decimalPlaces: 3 }).subscribe();
    expect(http.expectOne(UOM_API).request.body).toEqual({ code: 'KG', name: 'Kilogram', decimalPlaces: 3 });
    service.update(4, { name: ' Kg ', isActive: false, decimalPlaces: 2, version: 5 }).subscribe();
    expect(http.expectOne(`${UOM_API}/4`).request.body).toEqual({ name: 'Kg', isActive: false, decimalPlaces: 2, version: 5 });
    service.remove(4, 6).subscribe();
    expect(http.expectOne(`${UOM_API}/4?version=6`).request.method).toBe('DELETE');

    service.listConversions().subscribe();
    http.expectOne(UOM_CONVERSION_API).flush([]);
    service.createConversion({ fromUomId: 1, toUomId: 2, factor: '453.59237' }).subscribe();
    expect(http.expectOne(UOM_CONVERSION_API).request.body).toEqual({ fromUomId: 1, toUomId: 2, factor: 453.59237 });
    service.updateConversion(9, { factor: '999999999.999999', version: 2 }).subscribe();
    const put = http.expectOne(`${UOM_CONVERSION_API}/9`);
    expect(JSON.stringify(put.request.body)).toBe('{"factor":999999999.999999,"version":2}');
    service.removeConversion(9, 3).subscribe();
    http.expectOne(`${UOM_CONVERSION_API}/9?version=3`);
    service.convert(1, 2, ' 1.234567 ').subscribe();
    expect(http.expectOne((r) => r.url === `${UOM_CONVERSION_API}/convert`).request.params.toString())
      .toBe('fromUomId=1&toUomId=2&quantity=1.234567');
  });

  it('validates factors and quantities and only maps UoM error codes', () => {
    expect(['1', '0.000001', '999999999.999999', '12.5'].every((v) => FACTOR_PATTERN.test(v))).toBe(true);
    expect(['0', '0.000', '-1', '1.0000001', '1000000000', '1e3', ''].some((v) => FACTOR_PATTERN.test(v))).toBe(false);
    expect(['-1.5', '0', '999999999999.999999'].every((v) => QUANTITY_PATTERN.test(v))).toBe(true);
    expect(QUANTITY_PATTERN.test('1.1234567')).toBe(false);
    const error = (errorCode: string, status = 409) => new HttpErrorResponse({ status, error: { errorCode } });
    expect(service.errorKey(error('MD.UOM_CONVERSION.PATH_EXISTS'))).toBe('errors.MD.UOM_CONVERSION.PATH_EXISTS');
    expect(service.errorKey(error('MD.UOM_CONVERSION.NO_PATH', 422))).toBe('errors.MD.UOM_CONVERSION.NO_PATH');
    expect(service.errorKey(error('MD.UOM.IN_USE'))).toBe('errors.MD.UOM.IN_USE');
    expect(service.errorKey(error('MD.SUPPLIER.NOT_FOUND', 404))).toBe('errors.http.unknown');
  });
});
