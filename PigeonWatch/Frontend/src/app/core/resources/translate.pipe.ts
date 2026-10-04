import { Pipe, PipeTransform, inject } from '@angular/core';
import { ResourceService } from './resource.service';

@Pipe({ name: 'translate', pure: false })
export class TranslatePipe implements PipeTransform {
  private readonly resources = inject(ResourceService);

  transform(key: string, ...args: unknown[]): string {
    return this.resources.t(key, ...args);
  }
}
