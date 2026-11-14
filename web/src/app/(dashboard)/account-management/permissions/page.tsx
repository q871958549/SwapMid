import { Suspense } from 'react'
import { PermissionsClient } from './client'

export default function PermissionsPage() {
  return (
    <Suspense>
      <PermissionsClient />
    </Suspense>
  )
}
