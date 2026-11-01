import { Injectable, effect, inject } from '@angular/core';
import { MatPaginatorIntl } from '@angular/material/paginator';
import { ResourceService } from '../../../core/resources/resource.service';

@Injectable()
export class SightingListPaginatorIntl extends MatPaginatorIntl {
  private readonly resources = inject(ResourceService);

  constructor() {
    super();
    this.applyLabels();
    effect(() => {
      this.applyLabels();
      this.changes.next();
    });
  }

  override getRangeLabel = (): string => '';

  private applyLabels(): void {
    this.firstPageLabel = this.resources.translate('list.paginator.firstPage');
    this.previousPageLabel = this.resources.translate('list.paginator.previousPage');
    this.nextPageLabel = this.resources.translate('list.paginator.nextPage');
    this.lastPageLabel = this.resources.translate('list.paginator.lastPage');
  }
}
