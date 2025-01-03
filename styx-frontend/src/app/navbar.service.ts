import { Injectable } from '@angular/core';
import { Subject } from 'rxjs';

@Injectable({
  providedIn: 'root',
})
export class NavbarService {
  private refreshNavbarSubject = new Subject<void>();

  // Observable to trigger a navbar refresh
  refreshNavbar$ = this.refreshNavbarSubject.asObservable();

  triggerNavbarRefresh(): void {
    this.refreshNavbarSubject.next();
  }
}
